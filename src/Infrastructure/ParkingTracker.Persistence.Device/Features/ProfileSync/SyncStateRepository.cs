using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The repository of the <c>sync_state</c> row over sqlite-net (ADR-0032). Reads answer <see cref="SyncState.Empty"/> when
///              there is no row. Writes are only staged in the scope of the caller, in plain upserts that create the row when it is
///              missing, so that the moment of a change and the data it describes are committed together. The pending-removal flag of
///              the row is never touched here.
/// </summary>
public sealed partial class SyncStateRepository : ISyncStateRepository
{
	#region Fields
	/// <summary>
	/// The database that holds the table.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<SyncStateRepository> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SyncStateRepository"/> class.
	/// </summary>
	/// <param name="database">The database that holds the table.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public SyncStateRepository(DeviceDatabase database, ILogger<SyncStateRepository> logger)
	{
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(logger);
		this.database = database;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a read that failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Reading the sync state failed")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);
	#endregion

	#region ISyncStateRepository Implementation
	/// <inheritdoc />
	public async Task<Result<SyncState>> GetAsync(CancellationToken cancellationToken = default)
	{
		List<SyncStateRow> rows;
		try
		{
			rows = await database.Connection.QueryAsync<SyncStateRow>("SELECT * FROM sync_state LIMIT 1").ConfigureAwait(false);
		}
		catch (SQLiteException exception)
		{
			LogReadFailed(logger, exception);
			return Result.Failure<SyncState>(DeviceDatabaseErrors.ReadFailed);
		}

		return rows.Count == 0 ? SyncState.Empty : Result.Success(ToState(rows[0]));
	}

	/// <inheritdoc />
	public void StageTouch(IDeviceChangeScope scope, SyncSection section, DateTimeOffset at)
	{
		var column = ColumnOf(section);
		var millis = at.ToUnixTimeMilliseconds();
		DeviceChangeScope.From(scope).Stage(connection => connection.Execute(
			$"INSERT INTO sync_state (id, dirty, {column}) VALUES (1, 1, ?) ON CONFLICT (id) DO UPDATE SET dirty = 1, {column} = excluded.{column}",
			millis));
	}

	/// <inheritdoc />
	public void StageState(IDeviceChangeScope scope, SyncState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		var times = state.Times;
		DeviceChangeScope.From(scope).Stage(connection => connection.Execute(
			"""
			INSERT INTO sync_state (id, base_revision, dirty, home_updated_at, work_updated_at, favourites_updated_at, vehicle_updated_at, card_updated_at)
			VALUES (1, ?, ?, ?, ?, ?, ?, ?)
			ON CONFLICT (id) DO UPDATE SET
				base_revision = excluded.base_revision,
				dirty = excluded.dirty,
				home_updated_at = excluded.home_updated_at,
				work_updated_at = excluded.work_updated_at,
				favourites_updated_at = excluded.favourites_updated_at,
				vehicle_updated_at = excluded.vehicle_updated_at,
				card_updated_at = excluded.card_updated_at
			""",
			state.BaseRevision,
			state.IsDirty ? 1 : 0,
			times.Home?.ToUnixTimeMilliseconds(),
			times.Work?.ToUnixTimeMilliseconds(),
			times.Favourites?.ToUnixTimeMilliseconds(),
			times.Vehicle?.ToUnixTimeMilliseconds(),
			times.Card?.ToUnixTimeMilliseconds()));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Maps a stored row to the state.
	/// </summary>
	/// <param name="row">The row.</param>
	/// <returns>The state.</returns>
	private static SyncState ToState(SyncStateRow row)
	{
		var times = new SectionTimes(At(row.HomeUpdatedAt), At(row.WorkUpdatedAt), At(row.FavouritesUpdatedAt), At(row.VehicleUpdatedAt), At(row.CardUpdatedAt));
		return new SyncState(row.BaseRevision, row.Dirty != 0, times);
	}

	/// <summary>
	/// Turns a stored moment into a date and time.
	/// </summary>
	/// <param name="millis">Milliseconds since 1970, or <see langword="null"/>.</param>
	/// <returns>The moment in UTC, or <see langword="null"/>.</returns>
	private static DateTimeOffset? At(long? millis) => millis is { } value ? DateTimeOffset.FromUnixTimeMilliseconds(value) : null;

	/// <summary>
	/// Gives the column that holds the moment of a section. The names are fixed here, never built from outside text.
	/// </summary>
	/// <param name="section">The section.</param>
	/// <returns>The column name.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="section"/> is not a known section.</exception>
	private static string ColumnOf(SyncSection section)
	{
		return section switch
		{
			SyncSection.Home => "home_updated_at",
			SyncSection.Work => "work_updated_at",
			SyncSection.Favourites => "favourites_updated_at",
			SyncSection.Vehicle => "vehicle_updated_at",
			SyncSection.Card => "card_updated_at",
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown section."),
		};
	}
	#endregion
}
