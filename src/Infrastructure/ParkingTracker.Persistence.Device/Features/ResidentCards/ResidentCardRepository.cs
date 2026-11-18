using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.ResidentCards.Aggregates;
using ParkingTracker.Domain.Features.ResidentCards.Errors;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.ResidentCards;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The hand-written repository of the residents' card over sqlite-net (ADR-0026). It maps the aggregate to a
///              <see cref="ResidentCardRow"/> and back, encrypting the card number and the plate with <see cref="FieldCipher"/> bound to
///              their column and to the identifier of the card, and rebuilds the card through <see cref="ResidentCard.Restore"/> against
///              the municipality of the reference data, so that a row that breaks a rule gives a failed <see cref="Result"/> and never
///              an invalid aggregate. A number or plate that cannot be decrypted fails the read, so that a card is never returned with
///              fields silently dropped; a key that cannot be read now is the failure <see cref="DeviceDataErrors.KeyUnavailable"/>, not
///              "no card". Changes are only staged in the scope of the operation; the unit of work applies them. The phone holds one
///              card: an add does nothing when a card exists, an update replaces it.
/// </summary>
public sealed partial class ResidentCardRepository : IResidentCardRepository
{
	#region Constants
	/// <summary>
	/// The name of the table, for the errors and the binding of the encrypted fields.
	/// </summary>
	private const string Table = "resident_cards";

	/// <summary>
	/// The name of the column of the encrypted card number.
	/// </summary>
	private const string NumberColumn = "card_number";

	/// <summary>
	/// The name of the column of the encrypted plate.
	/// </summary>
	private const string PlateColumn = "plate";
	#endregion

	#region Fields
	/// <summary>
	/// The database that holds the table.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// Encrypts the card number and the plate.
	/// </summary>
	private readonly FieldCipher cipher;

	/// <summary>
	/// The municipalities that a stored card is checked against.
	/// </summary>
	private readonly IReferenceData referenceData;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<ResidentCardRepository> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ResidentCardRepository"/> class.
	/// </summary>
	/// <param name="database">The database that holds the table.</param>
	/// <param name="cipher">Encrypts the card number and the plate.</param>
	/// <param name="referenceData">The municipalities that a stored card is checked against.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ResidentCardRepository(DeviceDatabase database, FieldCipher cipher, IReferenceData referenceData, ILogger<ResidentCardRepository> logger)
	{
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(cipher);
		ArgumentNullException.ThrowIfNull(referenceData);
		ArgumentNullException.ThrowIfNull(logger);
		this.database = database;
		this.cipher = cipher;
		this.referenceData = referenceData;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs an encrypted field that could not be read, which fails the read of the card.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="field">The name of the field.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The card field {Field} cannot be decrypted ({Code}); the card is not read")]
	private static partial void LogFieldUnreadable(ILogger logger, string field, string code);

	/// <summary>
	/// Logs a read that failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Reading the residents' card failed")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);
	#endregion

	#region IResidentCardRepository Implementation
	/// <inheritdoc />
	public async Task<Result<ResidentCard>> GetAsync(CancellationToken cancellationToken = default)
	{
		List<ResidentCardRow> rows;
		try
		{
			rows = await database.Connection.QueryAsync<ResidentCardRow>("SELECT * FROM resident_cards LIMIT 1").ConfigureAwait(false);
		}
		catch (SQLiteException exception)
		{
			LogReadFailed(logger, exception);
			return Result.Failure<ResidentCard>(DeviceDatabaseErrors.ReadFailed);
		}

		if (rows.Count == 0)
		{
			return Result.Failure<ResidentCard>(DeviceDataErrors.NotStored, ResultExceptionType.NotFound);
		}

		return await RebuildAsync(rows[0], cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<Result> AddAsync(IDeviceChangeScope scope, ResidentCard card, CancellationToken cancellationToken = default)
	{
		var target = DeviceChangeScope.From(scope);
		ArgumentNullException.ThrowIfNull(card);
		var row = await ToRowAsync(card, cancellationToken).ConfigureAwait(false);
		if (row.IsFailure)
		{
			return Result.Failure(row.Error, row.ResultExceptionType);
		}

		target.Stage(connection =>
		{
			if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM resident_cards") == 0)
			{
				_ = connection.Insert(row.Value);
			}
		});
		return Result.Success();
	}

	/// <inheritdoc />
	public async Task<Result> UpdateAsync(IDeviceChangeScope scope, ResidentCard card, CancellationToken cancellationToken = default)
	{
		var target = DeviceChangeScope.From(scope);
		ArgumentNullException.ThrowIfNull(card);
		var row = await ToRowAsync(card, cancellationToken).ConfigureAwait(false);
		if (row.IsFailure)
		{
			return Result.Failure(row.Error, row.ResultExceptionType);
		}

		target.Stage(connection =>
		{
			_ = connection.Execute("DELETE FROM resident_cards");
			_ = connection.Insert(row.Value);
		});
		return Result.Success();
	}

	/// <inheritdoc />
	public void Remove(IDeviceChangeScope scope) => DeviceChangeScope.From(scope).Stage(connection => connection.Execute("DELETE FROM resident_cards"));
	#endregion

	#region Private Helpers
	/// <summary>
	/// Maps a card to its row, encrypting the number and the plate.
	/// </summary>
	/// <param name="card">The card.</param>
	/// <param name="cancellationToken">Cancels the encryption.</param>
	/// <returns>A task with the row, or the failure of the encryption.</returns>
	private async Task<Result<ResidentCardRow>> ToRowAsync(ResidentCard card, CancellationToken cancellationToken)
	{
		var values = card.ToParkingCard();
		var id = card.Id.Value.ToString("D");
		var number = await EncryptOptionalAsync(values.Number, new FieldBinding(Table, NumberColumn, id), cancellationToken).ConfigureAwait(false);
		var plate = await EncryptOptionalAsync(values.Plate, new FieldBinding(Table, PlateColumn, id), cancellationToken).ConfigureAwait(false);
		if (number.IsFailure || plate.IsFailure)
		{
			var failed = number.IsFailure ? number : plate;
			return Result.Failure<ResidentCardRow>(failed.Error, failed.ResultExceptionType);
		}

		return new ResidentCardRow
		{
			Id = id,
			Municipality = values.Municipality,
			Sector = values.Sector,
			ValidFrom = StoredText.FormatDay(values.Period.ValidFrom),
			ValidTo = StoredText.FormatDay(values.Period.ValidTo),
			Tolerances = StoredText.FormatList(values.Tolerances),
			Mailles = StoredText.FormatList(values.Mailles),
			Lims = StoredText.FormatList(values.Lims),
			CardNumber = NullIfEmpty(number.Value),
			Plate = NullIfEmpty(plate.Value),
		};
	}

	/// <summary>
	/// Encrypts a field that may be missing.
	/// </summary>
	/// <param name="value">The text, or <see langword="null"/>.</param>
	/// <param name="binding">Where the value is stored.</param>
	/// <param name="cancellationToken">Cancels the encryption.</param>
	/// <returns>A task with the encrypted text, or an empty text for a missing field (a result cannot hold <see langword="null"/>;
	/// an encrypted value is never empty).</returns>
	private async Task<Result<string>> EncryptOptionalAsync(string? value, FieldBinding binding, CancellationToken cancellationToken)
	{
		return value is null ? Result.Success(string.Empty) : await cipher.EncryptAsync(value, binding, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Turns the empty text that marks a missing field into <see langword="null"/>.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <returns>The text, or <see langword="null"/> when it is empty.</returns>
	private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

	/// <summary>
	/// Rebuilds the aggregate from a row through its restore factory.
	/// </summary>
	/// <param name="row">The stored row.</param>
	/// <param name="cancellationToken">Cancels the decryption.</param>
	/// <returns>A task with the card, or the rule or the failure of the decryption that stops the read.</returns>
	private async Task<Result<ResidentCard>> RebuildAsync(ResidentCardRow row, CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(row.Id, out var id) || id == Guid.Empty)
		{
			return Result.Failure<ResidentCard>(DeviceDatabaseErrors.RowInvalid(Table, "the identifier cannot be read"));
		}

		if (!StoredText.TryParseDay(row.ValidFrom, out var from) || !StoredText.TryParseDay(row.ValidTo, out var to)
			|| !StoredText.TryParseList(row.Tolerances, out var tolerances) || !StoredText.TryParseList(row.Mailles, out var mailles)
			|| !StoredText.TryParseList(row.Lims, out var lims))
		{
			return Result.Failure<ResidentCard>(DeviceDatabaseErrors.RowInvalid(Table, "a date or a list cannot be read"));
		}

		var data = referenceData.GetReferenceData();
		if (data.IsFailure)
		{
			return Result.Failure<ResidentCard>(data.Error, data.ResultExceptionType);
		}

		var municipality = data.Value.Municipalities.FirstOrDefault(candidate => string.Equals(candidate.Id, row.Municipality, StringComparison.Ordinal));
		if (municipality is null)
		{
			return Result.Failure<ResidentCard>(ResidentCardErrors.MunicipalityMismatch);
		}

		var number = await DecryptOptionalAsync(row.CardNumber, new FieldBinding(Table, NumberColumn, row.Id), cancellationToken).ConfigureAwait(false);
		var plate = await DecryptOptionalAsync(row.Plate, new FieldBinding(Table, PlateColumn, row.Id), cancellationToken).ConfigureAwait(false);
		if (number.IsFailure || plate.IsFailure)
		{
			var failed = number.IsFailure ? number : plate;
			return Result.Failure<ResidentCard>(failed.Error, failed.ResultExceptionType);
		}

		var values = new ParkingCard(row.Municipality, row.Sector, new CardPeriod(from, to), tolerances, mailles, lims, NullIfEmpty(number.Value), NullIfEmpty(plate.Value));
		return ResidentCard.Restore(new ResidentCardId(id), municipality, values);
	}

	/// <summary>
	/// Decrypts a field that may be missing; a field that cannot be decrypted fails.
	/// </summary>
	/// <param name="stored">The stored text, or <see langword="null"/>.</param>
	/// <param name="binding">Where the value is stored.</param>
	/// <param name="cancellationToken">Cancels the decryption.</param>
	/// <returns>A task with the text, an empty text for a missing field, or the failure of the decryption.</returns>
	private async Task<Result<string>> DecryptOptionalAsync(string? stored, FieldBinding binding, CancellationToken cancellationToken)
	{
		if (stored is null)
		{
			return Result.Success(string.Empty);
		}

		var decrypted = await cipher.DecryptAsync(stored, binding, cancellationToken).ConfigureAwait(false);
		if (decrypted.IsFailure)
		{
			LogFieldUnreadable(logger, binding.Column, decrypted.Error.Code);
		}

		return decrypted;
	}
	#endregion
}
