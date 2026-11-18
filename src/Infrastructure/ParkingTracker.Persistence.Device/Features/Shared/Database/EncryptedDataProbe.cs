using Microsoft.Extensions.Logging;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Answers <see cref="IEncryptedDataProbe"/> over the real tables: a card with a number or a plate, any saved place and any
///              parking session hold encrypted values. A table that does not exist yet holds none, so a fresh phone gets its key; any other
///              failure to read is reported as a failure and not as "none".
/// </summary>
public sealed partial class EncryptedDataProbe : IEncryptedDataProbe
{
	#region Constants
	/// <summary>
	/// Counts the rows that hold an encrypted value.
	/// </summary>
	private const string CountSql = """
		SELECT (SELECT COUNT(*) FROM resident_cards WHERE card_number IS NOT NULL OR plate IS NOT NULL)
			+ (SELECT COUNT(*) FROM saved_places)
			+ (SELECT COUNT(*) FROM parking_sessions)
		""";
	#endregion

	#region Fields
	/// <summary>
	/// The database to ask.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<EncryptedDataProbe> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="EncryptedDataProbe"/> class.
	/// </summary>
	/// <param name="database">The database to ask.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public EncryptedDataProbe(DeviceDatabase database, ILogger<EncryptedDataProbe> logger)
	{
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(logger);
		this.database = database;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a count that failed, so that no key is created without knowing.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The encrypted rows of the device database could not be counted; no key is created")]
	private static partial void LogCountFailed(ILogger logger, Exception exception);
	#endregion

	#region IEncryptedDataProbe Implementation
	/// <inheritdoc />
	public async Task<Result<bool>> AnyAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			return await database.Connection.ExecuteScalarAsync<int>(CountSql).ConfigureAwait(false) > 0;
		}
		catch (SQLiteException exception) when (exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
		{
			// The schema was never created: nothing is stored, so nothing can be lost.
			return false;
		}
		catch (SQLiteException exception)
		{
			LogCountFailed(logger, exception);
			return Result.Failure<bool>(DeviceDatabaseErrors.ReadFailed);
		}
	}
	#endregion
}
