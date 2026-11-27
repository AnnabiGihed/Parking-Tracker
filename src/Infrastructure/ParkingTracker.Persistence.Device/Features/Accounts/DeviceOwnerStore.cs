using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The store of the device owner over sqlite-net (ADR-0032): it reads the hash of the bound account from the one row of
///              <c>device_owner</c> and binds a new hash in its own scope of the unit of work, so that the write waits for any other
///              writer. The flag of a pending removal lives in the <c>erase_pending</c> column of <c>sync_state</c>, which the full wipe
///              deletes with the row. It brings the schema up to date first, because the sign-in can run before anything else touched the database.
/// </summary>
public sealed partial class DeviceOwnerStore : IDeviceOwnerStore
{
	#region Fields
	/// <summary>
	/// The database that holds the table.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// The unit of work that serialises the writes.
	/// </summary>
	private readonly IDeviceUnitOfWork unitOfWork;

	/// <summary>
	/// Brings the schema up to date before the first read or write.
	/// </summary>
	private readonly IDeviceDatabaseInitializer initializer;

	/// <summary>
	/// Clock for the moment of the binding.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<DeviceOwnerStore> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceOwnerStore"/> class.
	/// </summary>
	/// <param name="database">The database that holds the table.</param>
	/// <param name="unitOfWork">The unit of work that serialises the writes.</param>
	/// <param name="initializer">Brings the schema up to date before the first read or write.</param>
	/// <param name="clock">Clock for the moment of the binding.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public DeviceOwnerStore(DeviceDatabase database, IDeviceUnitOfWork unitOfWork, IDeviceDatabaseInitializer initializer, TimeProvider clock, ILogger<DeviceOwnerStore> logger)
	{
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(initializer);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		this.database = database;
		this.unitOfWork = unitOfWork;
		this.initializer = initializer;
		this.clock = clock;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a read that failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Reading the device owner failed")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);
	#endregion

	#region IDeviceOwnerStore Implementation
	/// <inheritdoc />
	public async Task<Result<string>> GetAccountHashAsync(CancellationToken cancellationToken = default)
	{
		var ready = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
		if (ready.IsFailure)
		{
			return Result.Failure<string>(ready.Error);
		}

		List<DeviceOwnerRow> rows;
		try
		{
			rows = await database.Connection.QueryAsync<DeviceOwnerRow>("SELECT * FROM device_owner LIMIT 1").ConfigureAwait(false);
		}
		catch (SQLiteException exception)
		{
			LogReadFailed(logger, exception);
			return Result.Failure<string>(DeviceDatabaseErrors.ReadFailed);
		}

		return rows.Count == 0
			? Result.Failure<string>(DeviceDataErrors.NotStored, ResultExceptionType.NotFound)
			: Result.Success(rows[0].AccountHash);
	}

	/// <inheritdoc />
	public async Task<Result<bool>> GetErasePendingAsync(CancellationToken cancellationToken = default)
	{
		var ready = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
		if (ready.IsFailure)
		{
			return Result.Failure<bool>(ready.Error);
		}

		try
		{
			var flag = await database.Connection.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT erase_pending FROM sync_state WHERE id = 1), 0)").ConfigureAwait(false);
			return Result.Success(flag != 0);
		}
		catch (SQLiteException exception)
		{
			LogReadFailed(logger, exception);
			return Result.Failure<bool>(DeviceDatabaseErrors.ReadFailed);
		}
	}

	/// <inheritdoc />
	public async Task<Result> MarkErasePendingAsync(CancellationToken cancellationToken = default)
	{
		var ready = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
		if (ready.IsFailure)
		{
			return ready;
		}

		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			// The row is created when it is missing, because the flag may be the first thing the sync state holds.
			DeviceChangeScope.From(scope).Stage(connection => connection.Execute(
				"INSERT INTO sync_state (id, erase_pending) VALUES (1, 1) ON CONFLICT (id) DO UPDATE SET erase_pending = 1"));
			return await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async Task<Result> BindAsync(string accountHash, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(accountHash);
		var ready = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
		if (ready.IsFailure)
		{
			return ready;
		}

		var boundAt = clock.GetUtcNow().ToUnixTimeMilliseconds();
		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			// A replace keeps the one-row rule of the table when the phone is bound to another account.
			DeviceChangeScope.From(scope).Stage(connection => connection.Execute(
				"INSERT OR REPLACE INTO device_owner (id, account_hash, bound_at) VALUES (1, ?, ?)", accountHash, boundAt));
			return await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
		}
	}
	#endregion
}
