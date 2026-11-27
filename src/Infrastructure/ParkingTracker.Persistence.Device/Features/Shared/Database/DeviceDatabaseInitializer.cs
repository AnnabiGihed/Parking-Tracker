using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database.Schema;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Brings the schema of the device database up to date (ADR-0026). The <c>schema_version</c> table holds one row per
///              applied step; the initializer reads the highest version and applies each missing step in order, each step and its
///              version row in one transaction. A database whose version is higher than any step this app knows is refused before
///              anything is written. sqlite-net's automatic table creation is not used.
/// </summary>
public sealed partial class DeviceDatabaseInitializer : IDeviceDatabaseInitializer, IDisposable
{
	#region Static Instances
	/// <summary>
	/// The steps of this release, in order.
	/// </summary>
	private static readonly IDeviceSchemaStep[] ReleasedSteps = [new CreateTablesStep(), new AddVehiclesStep(), new AddSyncStateStep()];
	#endregion

	#region Fields
	/// <summary>
	/// The database to bring up to date.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// The steps to apply, in ascending order of their version.
	/// </summary>
	private readonly IReadOnlyList<IDeviceSchemaStep> steps;

	/// <summary>
	/// Clock for the time a step was applied.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<DeviceDatabaseInitializer> logger;

	/// <summary>
	/// Lets one run at a time check and change the schema.
	/// </summary>
	private readonly SemaphoreSlim runGate = new(1, 1);

	/// <summary>
	/// Whether a run of this process brought the schema up to date, so that later calls return at once.
	/// </summary>
	private volatile bool isCurrent;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceDatabaseInitializer"/> class with the steps of this release.
	/// </summary>
	/// <param name="database">The database to bring up to date.</param>
	/// <param name="clock">Clock for the time a step was applied.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public DeviceDatabaseInitializer(DeviceDatabase database, TimeProvider clock, ILogger<DeviceDatabaseInitializer> logger)
		: this(database, ReleasedSteps, clock, logger)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceDatabaseInitializer"/> class with the given steps; tests use it to run
	/// steps that fail or that this release does not know.
	/// </summary>
	/// <param name="database">The database to bring up to date.</param>
	/// <param name="steps">The steps, in ascending order of their version.</param>
	/// <param name="clock">Clock for the time a step was applied.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	internal DeviceDatabaseInitializer(DeviceDatabase database, IReadOnlyList<IDeviceSchemaStep> steps, TimeProvider clock, ILogger<DeviceDatabaseInitializer> logger)
	{
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(steps);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		this.database = database;
		this.steps = steps;
		this.clock = clock;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a step that was applied.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="version">The version of the step.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Applied step {Version} of the device database schema")]
	private static partial void LogStepApplied(ILogger logger, int version);

	/// <summary>
	/// Logs a database that a newer version of the app wrote.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="found">The version in the database.</param>
	/// <param name="known">The highest version this app knows.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The device database has schema version {Found}, but this app only knows {Known}")]
	private static partial void LogNewerThanApp(ILogger logger, int found, int known);

	/// <summary>
	/// Logs a step that failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="version">The version of the step.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Step {Version} of the device database schema failed")]
	private static partial void LogStepFailed(ILogger logger, int version, Exception exception);

	/// <summary>
	/// Logs a schema version that could not be read.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The schema version of the device database could not be read")]
	private static partial void LogVersionReadFailed(ILogger logger, Exception exception);
	#endregion

	#region IDeviceDatabaseInitializer Implementation
	/// <inheritdoc />
	public async Task<Result> InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (isCurrent)
		{
			return Result.Success();
		}

		await runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var result = isCurrent ? Result.Success() : await RunStepsAsync(cancellationToken).ConfigureAwait(false);
			isCurrent = result.IsSuccess;
			return result;
		}
		finally
		{
			_ = runGate.Release();
		}
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		runGate.Dispose();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the schema version and applies the missing steps.
	/// </summary>
	/// <param name="cancellationToken">Cancels the work between steps.</param>
	/// <returns>A task with success, or the failure of the first step that failed.</returns>
	private async Task<Result> RunStepsAsync(CancellationToken cancellationToken)
	{
		try
		{
			var connection = database.Connection;
			_ = await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL PRIMARY KEY, applied_at INTEGER NOT NULL)").ConfigureAwait(false);
			var current = await connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(version), 0) FROM schema_version").ConfigureAwait(false);
			var known = steps.Count == 0 ? 0 : steps[^1].Version;
			if (current > known)
			{
				LogNewerThanApp(logger, current, known);
				return Result.Failure(DeviceDatabaseErrors.SchemaNewerThanApp);
			}

			foreach (var step in steps.Where(step => step.Version > current))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var applied = await ApplyAsync(connection, step).ConfigureAwait(false);
				if (applied.IsFailure)
				{
					return applied;
				}
			}

			return Result.Success();
		}
		catch (SQLiteException exception)
		{
			LogVersionReadFailed(logger, exception);
			return Result.Failure(DeviceDatabaseErrors.ReadFailed);
		}
	}

	/// <summary>
	/// Applies one step and records it, in one transaction.
	/// </summary>
	/// <param name="connection">The connection.</param>
	/// <param name="step">The step to apply.</param>
	/// <returns>A task with success, or the failure of the step.</returns>
	private async Task<Result> ApplyAsync(SQLiteAsyncConnection connection, IDeviceSchemaStep step)
	{
		var appliedAt = clock.GetUtcNow().ToUnixTimeMilliseconds();
		try
		{
			await connection.RunInTransactionAsync(transaction =>
			{
				foreach (var statement in step.Statements)
				{
					_ = transaction.Execute(statement);
				}

				_ = transaction.Execute("INSERT INTO schema_version (version, applied_at) VALUES (?, ?)", step.Version, appliedAt);
			}).ConfigureAwait(false);
		}
		catch (SQLiteException exception)
		{
			LogStepFailed(logger, step.Version, exception);
			return Result.Failure(DeviceDatabaseErrors.SchemaStepFailed(step.Version));
		}

		LogStepApplied(logger, step.Version);
		return Result.Success();
	}
	#endregion
}
