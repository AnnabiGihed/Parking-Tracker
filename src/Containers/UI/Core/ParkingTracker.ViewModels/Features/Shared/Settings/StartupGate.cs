using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.ViewModels.Features.Shared.Settings.Database;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;

namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The start-up gate that the screens, the relay and the background alerts wait for before they read the saved values
///              (ADR-0008, ADR-0026). One run, shared by every caller, does three things in order: the copy of the Capacitor data into
///              the settings (<see cref="LegacyDataMigration"/>), the schema of the device database
///              (<see cref="IDeviceDatabaseInitializer"/>) and the move of the card, car and timer into the database
///              (<see cref="SettingsToDatabaseMigration"/>). It never throws and every step is bounded by its own time limit, so the
///              page always opens; when the schema cannot be brought up to date the move is skipped and the problem is logged.
/// </summary>
public sealed partial class StartupGate : IStartupGate
{
	#region Fields
	/// <summary>
	/// The copy of the data of the previous version.
	/// </summary>
	private readonly LegacyDataMigration legacy;

	/// <summary>
	/// Brings the schema of the device database up to date.
	/// </summary>
	private readonly IDeviceDatabaseInitializer initializer;

	/// <summary>
	/// The move of the saved values into the device database.
	/// </summary>
	private readonly SettingsToDatabaseMigration database;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<StartupGate> logger;

	/// <summary>
	/// Guards <see cref="run"/>.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// The run of this process, shared by every caller.
	/// </summary>
	private Task? run;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="StartupGate"/> class.
	/// </summary>
	/// <param name="legacy">The copy of the data of the previous version.</param>
	/// <param name="initializer">Brings the schema of the device database up to date.</param>
	/// <param name="database">The move of the saved values into the device database.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public StartupGate(LegacyDataMigration legacy, IDeviceDatabaseInitializer initializer, SettingsToDatabaseMigration database, ILogger<StartupGate> logger)
	{
		ArgumentNullException.ThrowIfNull(legacy);
		ArgumentNullException.ThrowIfNull(initializer);
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(logger);
		this.legacy = legacy;
		this.initializer = initializer;
		this.database = database;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a schema that could not be brought up to date.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The device database is not ready ({Code}); the saved values are not moved into it")]
	private static partial void LogDatabaseNotReady(ILogger logger, string code);

	/// <summary>
	/// Logs a gate that failed unexpectedly.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Preparing the device database failed")]
	private static partial void LogFailed(ILogger logger, Exception exception);
	#endregion

	#region IStartupGate Implementation
	/// <inheritdoc />
	public Task RunOnceAsync()
	{
		lock (gate)
		{
			return run ??= Task.Run(RunAsync);
		}
	}

	/// <inheritdoc />
	public async Task<bool> IsMoveFinishedAsync() => await database.ReadMarkerAsync().ConfigureAwait(false) is not null;
	#endregion

	#region Private Helpers
	/// <summary>
	/// Runs the three steps in order.
	/// </summary>
	/// <returns>A task that completes when the steps are done.</returns>
	private async Task RunAsync()
	{
		try
		{
			_ = await legacy.RunOnceAsync().ConfigureAwait(false);
			var ready = await initializer.InitializeAsync().ConfigureAwait(false);
			if (ready.IsFailure)
			{
				LogDatabaseNotReady(logger, ready.Error.Code);
				return;
			}

			_ = await database.RunOnceAsync().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogFailed(logger, exception);
		}
	}
	#endregion
}
