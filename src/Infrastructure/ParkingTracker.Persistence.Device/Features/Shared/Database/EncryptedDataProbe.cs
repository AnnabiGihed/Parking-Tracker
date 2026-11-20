using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Answers <see cref="IEncryptedDataProbe"/> over the real tables: a card with a number or a plate, any saved place, any
///              parking session and a vehicle profile with a plate hold encrypted values. Each table is counted only when it exists: a
///              table that does not exist yet holds none, so a fresh phone gets its key, and a phone whose schema is not yet at the
///              latest step still finds the encrypted rows of the tables it has. Any other failure to read is reported as a failure
///              and not as "none".
/// </summary>
public sealed partial class EncryptedDataProbe : IEncryptedDataProbe
{
	#region Static Instances
	/// <summary>
	/// For each table that can hold an encrypted value, the SQL that counts its rows holding one.
	/// </summary>
	private static readonly FrozenDictionary<string, string> CountSqlByTable = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["resident_cards"] = "SELECT COUNT(*) FROM resident_cards WHERE card_number IS NOT NULL OR plate IS NOT NULL",
		["saved_places"] = "SELECT COUNT(*) FROM saved_places",
		["parking_sessions"] = "SELECT COUNT(*) FROM parking_sessions",
		["vehicles"] = "SELECT COUNT(*) FROM vehicles WHERE plate IS NOT NULL",
	}.ToFrozenDictionary(StringComparer.Ordinal);
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
			var tables = await database.Connection.QueryScalarsAsync<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ConfigureAwait(false);
			foreach (var table in tables.Where(CountSqlByTable.ContainsKey))
			{
				if (await database.Connection.ExecuteScalarAsync<int>(CountSqlByTable[table]).ConfigureAwait(false) > 0)
				{
					return true;
				}
			}

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
