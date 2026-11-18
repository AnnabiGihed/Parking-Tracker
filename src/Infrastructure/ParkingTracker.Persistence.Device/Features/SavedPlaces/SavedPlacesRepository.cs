using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.SavedPlaces;

// An alias inside the namespace: the folder name SavedPlaces would otherwise win over the aggregate of the same name.
using PlaceList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The hand-written repository of the saved places over sqlite-net (ADR-0026). It maps each place to a
///              <see cref="SavedPlaceRow"/> with its coordinates encrypted by <see cref="FieldCipher"/> and bound to their column and
///              to the identifier of the place, and rebuilds the list through <c>SavedPlaces.Restore</c>, so that rows that break a
///              rule (a place outside the Brussels box, a second home, a 21st favourite) give a failed <see cref="Result"/>. A place
///              whose coordinates cannot be decrypted fails the read, and a key that cannot be read now is the failure
///              <see cref="DeviceDataErrors.KeyUnavailable"/>: a place is never left out silently, because the next save of the list
///              would then erase it. Changes are only staged in the scope of the operation; the unit of work applies them. An add
///              does nothing when places are stored, an update replaces them.
/// </summary>
public sealed partial class SavedPlacesRepository : ISavedPlacesRepository
{
	#region Constants
	/// <summary>
	/// The name of the table, for the errors and the binding of the encrypted coordinates.
	/// </summary>
	private const string Table = "saved_places";

	/// <summary>
	/// The name of the column of the encrypted coordinates.
	/// </summary>
	private const string CoordinatesColumn = "coordinates";
	#endregion

	#region Fields
	/// <summary>
	/// The database that holds the table.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// Encrypts the coordinates.
	/// </summary>
	private readonly FieldCipher cipher;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<SavedPlacesRepository> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SavedPlacesRepository"/> class.
	/// </summary>
	/// <param name="database">The database that holds the table.</param>
	/// <param name="cipher">Encrypts the coordinates.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public SavedPlacesRepository(DeviceDatabase database, FieldCipher cipher, ILogger<SavedPlacesRepository> logger)
	{
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(cipher);
		ArgumentNullException.ThrowIfNull(logger);
		this.database = database;
		this.cipher = cipher;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a place whose coordinates could not be read, which fails the read of the list.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "A saved place cannot be decrypted ({Code}); the saved places are not read")]
	private static partial void LogPlaceUnreadable(ILogger logger, string code);

	/// <summary>
	/// Logs a read that failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Reading the saved places failed")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);
	#endregion

	#region ISavedPlacesRepository Implementation
	/// <inheritdoc />
	public async Task<Result<PlaceList>> GetAsync(CancellationToken cancellationToken = default)
	{
		List<SavedPlaceRow> rows;
		try
		{
			rows = await database.Connection.QueryAsync<SavedPlaceRow>("SELECT * FROM saved_places ORDER BY position").ConfigureAwait(false);
		}
		catch (SQLiteException exception)
		{
			LogReadFailed(logger, exception);
			return Result.Failure<PlaceList>(DeviceDatabaseErrors.ReadFailed);
		}

		if (rows.Count == 0)
		{
			return PlaceList.Create();
		}

		var stored = new List<StoredPlace>();
		foreach (var row in rows)
		{
			var place = await ReadRowAsync(row, cancellationToken).ConfigureAwait(false);
			if (place.IsFailure)
			{
				return Result.Failure<PlaceList>(place.Error, place.ResultExceptionType);
			}

			stored.Add(place.Value);
		}

		return ListIdOf(rows) is { } listId
			? PlaceList.Restore(new SavedPlacesId(listId), stored)
			: Result.Failure<PlaceList>(DeviceDatabaseErrors.RowInvalid(Table, "the places do not belong to one list"));
	}

	/// <inheritdoc />
	public async Task<Result> AddAsync(IDeviceChangeScope scope, PlaceList places, CancellationToken cancellationToken = default)
	{
		var target = DeviceChangeScope.From(scope);
		ArgumentNullException.ThrowIfNull(places);
		var rows = await ToRowsAsync(places, cancellationToken).ConfigureAwait(false);
		if (rows.IsFailure)
		{
			return Result.Failure(rows.Error, rows.ResultExceptionType);
		}

		target.Stage(connection =>
		{
			if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM saved_places") == 0)
			{
				_ = connection.InsertAll(rows.Value, runInTransaction: false);
			}
		});
		return Result.Success();
	}

	/// <inheritdoc />
	public async Task<Result> UpdateAsync(IDeviceChangeScope scope, PlaceList places, CancellationToken cancellationToken = default)
	{
		var target = DeviceChangeScope.From(scope);
		ArgumentNullException.ThrowIfNull(places);
		var rows = await ToRowsAsync(places, cancellationToken).ConfigureAwait(false);
		if (rows.IsFailure)
		{
			return Result.Failure(rows.Error, rows.ResultExceptionType);
		}

		target.Stage(connection =>
		{
			_ = connection.Execute("DELETE FROM saved_places");
			_ = connection.InsertAll(rows.Value, runInTransaction: false);
		});
		return Result.Success();
	}

	/// <inheritdoc />
	public void Remove(IDeviceChangeScope scope) => DeviceChangeScope.From(scope).Stage(connection => connection.Execute("DELETE FROM saved_places"));
	#endregion

	#region Private Helpers
	/// <summary>
	/// Maps the places to rows, encrypting each pair of coordinates.
	/// </summary>
	/// <param name="places">The list.</param>
	/// <param name="cancellationToken">Cancels the encryption.</param>
	/// <returns>A task with the rows in saved order, or the failure of the encryption.</returns>
	private async Task<Result<List<SavedPlaceRow>>> ToRowsAsync(PlaceList places, CancellationToken cancellationToken)
	{
		var rows = new List<SavedPlaceRow>();
		foreach (var place in places.Places)
		{
			var id = place.Id.Value.ToString("D");
			var coordinates = await cipher.EncryptAsync(StoredText.FormatPoint(place.Point), new FieldBinding(Table, CoordinatesColumn, id), cancellationToken).ConfigureAwait(false);
			if (coordinates.IsFailure)
			{
				return Result.Failure<List<SavedPlaceRow>>(coordinates.Error, coordinates.ResultExceptionType);
			}

			rows.Add(new SavedPlaceRow
			{
				Id = id,
				ListId = places.Id.Value.ToString("D"),
				Position = rows.Count,
				Kind = (int)place.Kind,
				Label = place.Label,
				AddressText = place.AddressText,
				Coordinates = coordinates.Value,
			});
		}

		return rows;
	}

	/// <summary>
	/// Reads one stored place: its identifier, its decrypted coordinates and its texts.
	/// </summary>
	/// <param name="row">The stored place.</param>
	/// <param name="cancellationToken">Cancels the decryption.</param>
	/// <returns>A task with the place, or the failure of the decryption or of the row.</returns>
	private async Task<Result<StoredPlace>> ReadRowAsync(SavedPlaceRow row, CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(row.Id, out var id) || id == Guid.Empty)
		{
			return Result.Failure<StoredPlace>(DeviceDatabaseErrors.RowInvalid(Table, "the identifier of the place cannot be read"));
		}

		var plain = await cipher.DecryptAsync(row.Coordinates, new FieldBinding(Table, CoordinatesColumn, row.Id), cancellationToken).ConfigureAwait(false);
		if (plain.IsFailure)
		{
			LogPlaceUnreadable(logger, plain.Error.Code);
			return Result.Failure<StoredPlace>(plain.Error, plain.ResultExceptionType);
		}

		return StoredText.TryParsePoint(plain.Value, out var point)
			? new StoredPlace(new SavedPlaceId(id), (PlaceKind)row.Kind, row.Label, point, row.AddressText)
			: Result.Failure<StoredPlace>(DeviceDatabaseErrors.RowInvalid(Table, "the coordinates cannot be read"));
	}

	/// <summary>
	/// Reads the identifier of the list that every row names.
	/// </summary>
	/// <param name="rows">The stored places.</param>
	/// <returns>The identifier, or <see langword="null"/> when the rows name different lists or an identifier that is not valid.</returns>
	private static Guid? ListIdOf(List<SavedPlaceRow> rows)
	{
		var first = rows[0].ListId;
		return Guid.TryParse(first, out var id) && id != Guid.Empty && rows.All(row => string.Equals(row.ListId, first, StringComparison.Ordinal)) ? id : null;
	}
	#endregion
}
