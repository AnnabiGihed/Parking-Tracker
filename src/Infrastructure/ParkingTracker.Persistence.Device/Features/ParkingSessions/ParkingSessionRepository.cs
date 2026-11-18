using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.ParkingSessions.Aggregates;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.ParkingSessions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The hand-written repository of the parking session over sqlite-net (ADR-0026). It maps the session to a
///              <see cref="ParkingSessionRow"/> with the position encrypted by <see cref="FieldCipher"/> and bound to its column and
///              to the identifier of the session, and rebuilds it through <see cref="ParkingSession.Restore"/>, so that a row that
///              breaks a rule (a position that is not on Earth, a timer outside 5 minutes to one day, one that ends before the
///              parking moment) gives a failed <see cref="Result"/>. A position that cannot be decrypted fails the read; a key that
///              cannot be read now is the failure <see cref="DeviceDataErrors.KeyUnavailable"/>, never "no car saved". Changes are
///              only staged in the scope of the operation; the unit of work applies them. The phone holds one session: an add does
///              nothing when a session exists, an update replaces it.
/// </summary>
public sealed partial class ParkingSessionRepository : IParkingSessionRepository
{
	#region Constants
	/// <summary>
	/// The name of the table, for the errors and the binding of the encrypted position.
	/// </summary>
	private const string Table = "parking_sessions";

	/// <summary>
	/// The name of the column of the encrypted position.
	/// </summary>
	private const string CoordinatesColumn = "coordinates";
	#endregion

	#region Fields
	/// <summary>
	/// The database that holds the table.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// Encrypts the position.
	/// </summary>
	private readonly FieldCipher cipher;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<ParkingSessionRepository> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingSessionRepository"/> class.
	/// </summary>
	/// <param name="database">The database that holds the table.</param>
	/// <param name="cipher">Encrypts the position.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ParkingSessionRepository(DeviceDatabase database, FieldCipher cipher, ILogger<ParkingSessionRepository> logger)
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
	/// Logs a position that could not be read, which fails the read of the session.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The position of the parking session cannot be decrypted ({Code}); the session is not read")]
	private static partial void LogPositionUnreadable(ILogger logger, string code);

	/// <summary>
	/// Logs a read that failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Reading the parking session failed")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);
	#endregion

	#region IParkingSessionRepository Implementation
	/// <inheritdoc />
	public async Task<Result<ParkingSession>> GetAsync(CancellationToken cancellationToken = default)
	{
		List<ParkingSessionRow> rows;
		try
		{
			rows = await database.Connection.QueryAsync<ParkingSessionRow>("SELECT * FROM parking_sessions LIMIT 1").ConfigureAwait(false);
		}
		catch (SQLiteException exception)
		{
			LogReadFailed(logger, exception);
			return Result.Failure<ParkingSession>(DeviceDatabaseErrors.ReadFailed);
		}

		if (rows.Count == 0)
		{
			return Result.Failure<ParkingSession>(DeviceDataErrors.NotStored, ResultExceptionType.NotFound);
		}

		return await RebuildAsync(rows[0], cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<Result> AddAsync(IDeviceChangeScope scope, ParkingSession session, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		var target = DeviceChangeScope.From(scope);
		var row = await ToRowAsync(session, cancellationToken).ConfigureAwait(false);
		if (row.IsFailure)
		{
			return Result.Failure(row.Error, row.ResultExceptionType);
		}

		target.Stage(connection =>
		{
			if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM parking_sessions") == 0)
			{
				_ = connection.Insert(row.Value);
			}
		});
		return Result.Success();
	}

	/// <inheritdoc />
	public async Task<Result> UpdateAsync(IDeviceChangeScope scope, ParkingSession session, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		var target = DeviceChangeScope.From(scope);
		var row = await ToRowAsync(session, cancellationToken).ConfigureAwait(false);
		if (row.IsFailure)
		{
			return Result.Failure(row.Error, row.ResultExceptionType);
		}

		target.Stage(connection =>
		{
			_ = connection.Execute("DELETE FROM parking_sessions");
			_ = connection.Insert(row.Value);
		});
		return Result.Success();
	}

	/// <inheritdoc />
	public void Remove(IDeviceChangeScope scope) => DeviceChangeScope.From(scope).Stage(connection => connection.Execute("DELETE FROM parking_sessions"));
	#endregion

	#region Private Helpers
	/// <summary>
	/// Maps a session to its row, encrypting the position.
	/// </summary>
	/// <param name="session">The session.</param>
	/// <param name="cancellationToken">Cancels the encryption.</param>
	/// <returns>A task with the row, or the failure of the encryption.</returns>
	private async Task<Result<ParkingSessionRow>> ToRowAsync(ParkingSession session, CancellationToken cancellationToken)
	{
		var id = session.Id.Value.ToString("D");
		var coordinates = await cipher.EncryptAsync(StoredText.FormatPoint(session.Position), new FieldBinding(Table, CoordinatesColumn, id), cancellationToken).ConfigureAwait(false);
		if (coordinates.IsFailure)
		{
			return Result.Failure<ParkingSessionRow>(coordinates.Error, coordinates.ResultExceptionType);
		}

		return new ParkingSessionRow
		{
			Id = id,
			Coordinates = coordinates.Value,
			ParkedAt = session.ParkedAt.ToUnixTimeMilliseconds(),
			Headline = session.Headline,
			AddressText = session.AddressText,
			TimerEnd = session.Timer?.End.ToUnixTimeMilliseconds(),
			TimerMinutes = session.Timer?.Minutes,
			EndedAt = session.EndedAt?.ToUnixTimeMilliseconds(),
		};
	}

	/// <summary>
	/// Rebuilds the aggregate from a row through its restore factory.
	/// </summary>
	/// <param name="row">The stored row.</param>
	/// <param name="cancellationToken">Cancels the decryption.</param>
	/// <returns>A task with the session, the failure of the decryption when the position cannot be decrypted, or the rule the row breaks.</returns>
	private async Task<Result<ParkingSession>> RebuildAsync(ParkingSessionRow row, CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(row.Id, out var id) || id == Guid.Empty)
		{
			return Result.Failure<ParkingSession>(DeviceDatabaseErrors.RowInvalid(Table, "the identifier cannot be read"));
		}

		var plain = await cipher.DecryptAsync(row.Coordinates, new FieldBinding(Table, CoordinatesColumn, row.Id), cancellationToken).ConfigureAwait(false);
		if (plain.IsFailure)
		{
			LogPositionUnreadable(logger, plain.Error.Code);
			return Result.Failure<ParkingSession>(plain.Error, plain.ResultExceptionType);
		}

		if (!StoredText.TryParsePoint(plain.Value, out var point) || !TryReadMoments(row, out var parkedAt, out var timer, out var endedAt))
		{
			return Result.Failure<ParkingSession>(DeviceDatabaseErrors.RowInvalid(Table, "the coordinates or a moment cannot be read"));
		}

		return ParkingSession.Restore(new ParkingSessionId(id), point, parkedAt, row.Headline, row.AddressText, timer, endedAt);
	}

	/// <summary>
	/// Reads the moments of a row, which are milliseconds since 1970.
	/// </summary>
	/// <param name="row">The stored row.</param>
	/// <param name="parkedAt">When the car was parked.</param>
	/// <param name="timer">The stored timer, or <see langword="null"/> when none runs.</param>
	/// <param name="endedAt">When the user left the car, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when every stored moment is a date that exists.</returns>
	private static bool TryReadMoments(ParkingSessionRow row, out DateTimeOffset parkedAt, out ParkingTimer? timer, out DateTimeOffset? endedAt)
	{
		parkedAt = default;
		timer = null;
		endedAt = null;
		try
		{
			parkedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.ParkedAt);
			if (row.TimerEnd is { } end && row.TimerMinutes is { } minutes)
			{
				timer = new ParkingTimer(DateTimeOffset.FromUnixTimeMilliseconds(end), minutes);
			}

			endedAt = row.EndedAt is { } ended ? DateTimeOffset.FromUnixTimeMilliseconds(ended) : null;
			return true;
		}
		catch (ArgumentOutOfRangeException)
		{
			return false;
		}
	}
	#endregion
}
