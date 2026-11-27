using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Database.Schema;
using ParkingTracker.Testing.Device;

namespace ParkingTracker.Persistence.Device.Tests.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="DeviceDatabaseInitializer"/> against a real SQLite file: a fresh file gets every released
///              step and its version rows, a file at version 1 gets steps 2 and 3 and a file at version 2 gets step 3, each with
///              its data kept, a current file is left alone, a file written by a newer app is refused before anything is written,
///              and a step that fails is rolled back with its version row.
/// </summary>
public sealed class DeviceDatabaseInitializerTests
{
	#region Test Infrastructure
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : A schema step with the SQL a test gives it, to drive the initializer through fresh, current and failing files.
	/// </summary>
	private sealed class ScriptedStep : IDeviceSchemaStep
	{
		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="ScriptedStep"/> class.
		/// </summary>
		/// <param name="version">The number of the step.</param>
		/// <param name="statements">The SQL of the step.</param>
		public ScriptedStep(int version, params string[] statements)
		{
			Version = version;
			Statements = statements;
		}
		#endregion

		#region IDeviceSchemaStep Implementation
		/// <inheritdoc />
		public int Version { get; }

		/// <inheritdoc />
		public IReadOnlyList<string> Statements { get; }
		#endregion
	}
	#endregion

	#region Fresh Database Tests
	/// <summary>
	/// Verifies that a fresh file gets all tables of the released steps and one version row per step stamped with the clock.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_FreshFile_ShouldCreateTheTablesAndRecordTheLatestVersion()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var result = await db.Initializer.InitializeAsync();

		result.IsSuccess.Should().BeTrue();
		foreach (var table in new[] { "resident_cards", "saved_places", "parking_sessions", "device_owner", "vehicles", "sync_state", "schema_version" })
		{
			(await db.ScalarAsync<int>($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'")).Should().Be(1, table);
		}

		(await db.ScalarAsync<int>("SELECT MAX(version) FROM schema_version")).Should().Be(3);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM schema_version")).Should().Be(3);
		(await db.ScalarAsync<long>("SELECT MIN(applied_at) FROM schema_version")).Should().Be(db.Clock.GetUtcNow().ToUnixTimeMilliseconds());
	}

	/// <summary>
	/// Verifies that the device owner table holds at most one row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_FreshFile_ShouldLimitTheOwnerTableToOneRow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ExecuteAsync("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (1, 'abc', 1)");

		var second = () => db.ExecuteAsync("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (2, 'def', 1)");

		await second.Should().ThrowAsync<SQLite.SQLiteException>();
	}

	/// <summary>
	/// Verifies that the vehicles table holds at most one profile and fills the defaults of its columns.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_FreshFile_ShouldLimitTheVehiclesTableToOneRow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ExecuteAsync("INSERT INTO vehicles (id, make, model_year, fuel) VALUES ('a', 'bmw', 2020, 0)");

		var second = () => db.ExecuteAsync("INSERT INTO vehicles (id, make, model_year, fuel) VALUES ('b', 'audi', 2021, 1)");

		await second.Should().ThrowAsync<SQLite.SQLiteException>();
		(await db.ScalarAsync<string>("SELECT connectors FROM vehicles")).Should().Be("[]");
		(await db.ScalarAsync<int>("SELECT euro_norm FROM vehicles")).Should().Be(0);
	}
	#endregion

	#region Upgrade Tests
	/// <summary>
	/// Verifies that a file that a release with only step 1 wrote gets step 2 (the vehicles table) on top, and keeps its data.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_VersionOneFile_ShouldAddTheVehiclesTableAndKeepTheData()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		using (var firstRelease = new DeviceDatabaseInitializer(db.Database, [new CreateTablesStep()], db.Clock, NullLogger<DeviceDatabaseInitializer>.Instance))
		{
			(await firstRelease.InitializeAsync()).IsSuccess.Should().BeTrue();
		}

		(await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()))).IsSuccess.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'vehicles'")).Should().Be(0);
		db.Clock.Advance(TimeSpan.FromDays(30));

		var result = await db.Initializer.InitializeAsync();

		result.IsSuccess.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT MAX(version) FROM schema_version")).Should().Be(3);
		(await db.ScalarAsync<long>("SELECT applied_at FROM schema_version WHERE version = 2")).Should().Be(db.Clock.GetUtcNow().ToUnixTimeMilliseconds());
		(await db.Cards.GetAsync()).IsSuccess.Should().BeTrue();
		(await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.ElectricVehicle()))).IsSuccess.Should().BeTrue();
		(await db.Vehicles.GetAsync()).IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a file that a release with steps 1 and 2 wrote gets step 3 (the sync state table) on top, and keeps its data.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_VersionTwoFile_ShouldAddTheSyncStateTableAndKeepTheData()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		using (var secondRelease = new DeviceDatabaseInitializer(db.Database, [new CreateTablesStep(), new AddVehiclesStep()], db.Clock, NullLogger<DeviceDatabaseInitializer>.Instance))
		{
			(await secondRelease.InitializeAsync()).IsSuccess.Should().BeTrue();
		}

		(await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()))).IsSuccess.Should().BeTrue();
		(await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.ElectricVehicle()))).IsSuccess.Should().BeTrue();
		await db.ExecuteAsync("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (1, 'abc', 1)");
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'sync_state'")).Should().Be(0);
		db.Clock.Advance(TimeSpan.FromDays(30));

		var result = await db.Initializer.InitializeAsync();

		result.IsSuccess.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT MAX(version) FROM schema_version")).Should().Be(3);
		(await db.ScalarAsync<long>("SELECT applied_at FROM schema_version WHERE version = 3")).Should().Be(db.Clock.GetUtcNow().ToUnixTimeMilliseconds());
		(await db.Cards.GetAsync()).IsSuccess.Should().BeTrue();
		(await db.Vehicles.GetAsync()).IsSuccess.Should().BeTrue();
		(await db.ScalarAsync<string>("SELECT account_hash FROM device_owner")).Should().Be("abc");
	}

	/// <summary>
	/// Verifies that the sync state table holds one row at most, with a clean state and no times by default.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_FreshFile_ShouldLimitTheSyncStateTableToOneCleanRow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ExecuteAsync("INSERT INTO sync_state (id) VALUES (1)");

		var second = () => db.ExecuteAsync("INSERT INTO sync_state (id) VALUES (2)");

		await second.Should().ThrowAsync<SQLite.SQLiteException>();
		(await db.ScalarAsync<int>("SELECT dirty FROM sync_state")).Should().Be(0);
		(await db.ScalarAsync<int>("SELECT erase_pending FROM sync_state")).Should().Be(0);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM sync_state WHERE base_revision IS NULL AND home_updated_at IS NULL AND work_updated_at IS NULL AND favourites_updated_at IS NULL AND vehicle_updated_at IS NULL AND card_updated_at IS NULL")).Should().Be(1);
	}
	#endregion

	#region Current Database Tests
	/// <summary>
	/// Verifies that a second run, also by a new initializer over the same file, changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_AlreadyCurrent_ShouldChangeNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		db.Clock.Advance(TimeSpan.FromHours(1));
		using var again = new DeviceDatabaseInitializer(db.Database, db.Clock, NullLogger<DeviceDatabaseInitializer>.Instance);

		var result = await again.InitializeAsync();

		result.IsSuccess.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM schema_version")).Should().Be(3);
		(await db.ScalarAsync<long>("SELECT MAX(applied_at) FROM schema_version")).Should().BeLessThan(db.Clock.GetUtcNow().ToUnixTimeMilliseconds());
	}

	/// <summary>
	/// Verifies that a later step is applied on top of the current schema, and only that step.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_NewStep_ShouldApplyOnlyTheMissingStep()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		using var next = new DeviceDatabaseInitializer(db.Database, [new CreateTablesStep(), new AddVehiclesStep(), new AddSyncStateStep(), new ScriptedStep(4, "ALTER TABLE saved_places ADD COLUMN note TEXT NULL")], db.Clock, NullLogger<DeviceDatabaseInitializer>.Instance);

		var result = await next.InitializeAsync();

		result.IsSuccess.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT MAX(version) FROM schema_version")).Should().Be(4);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM pragma_table_info('saved_places') WHERE name = 'note'")).Should().Be(1);
	}
	#endregion

	#region Refusal Tests
	/// <summary>
	/// Verifies that a database whose version is higher than any step of this app is refused and nothing is written to it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_NewerThanApp_ShouldRefuseAndWriteNothing()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		await db.ExecuteAsync("CREATE TABLE schema_version (version INTEGER NOT NULL PRIMARY KEY, applied_at INTEGER NOT NULL)");
		await db.ExecuteAsync("INSERT INTO schema_version (version, applied_at) VALUES (99, 1)");

		var result = await db.Initializer.InitializeAsync();

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(DeviceDatabaseErrors.SchemaNewerThanApp);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'resident_cards'")).Should().Be(0);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM schema_version")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that a step that fails halfway is rolled back with its version row, and the failure names the step.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_FailingStep_ShouldRollBackTheStepAndItsVersionRow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var broken = new ScriptedStep(4, "CREATE TABLE half_done (id INTEGER)", "THIS IS NOT SQL");
		using var next = new DeviceDatabaseInitializer(db.Database, [new CreateTablesStep(), new AddVehiclesStep(), new AddSyncStateStep(), broken], db.Clock, NullLogger<DeviceDatabaseInitializer>.Instance);

		var result = await next.InitializeAsync();

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(DeviceDatabaseErrors.SchemaStepFailed(4));
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'half_done'")).Should().Be(0);
		(await db.ScalarAsync<int>("SELECT MAX(version) FROM schema_version")).Should().Be(3);
	}

	/// <summary>
	/// Verifies that a failed run is not remembered as current: a later run tries the step again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_AfterFailure_ShouldTryAgain()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ExecuteAsync("CREATE TABLE blocker (id INTEGER)");
		using var next = new DeviceDatabaseInitializer(db.Database, [new CreateTablesStep(), new AddVehiclesStep(), new AddSyncStateStep(), new ScriptedStep(4, "CREATE TABLE blocker (id INTEGER)")], db.Clock, NullLogger<DeviceDatabaseInitializer>.Instance);
		(await next.InitializeAsync()).IsFailure.Should().BeTrue();
		await db.ExecuteAsync("DROP TABLE blocker");

		var second = await next.InitializeAsync();

		second.IsSuccess.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT MAX(version) FROM schema_version")).Should().Be(4);
	}

	/// <summary>
	/// Verifies that cancelling between steps stops the run with a cancellation.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Initialize_Cancelled_ShouldThrowOperationCanceled()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		using var cancelled = new CancellationTokenSource();
		await cancelled.CancelAsync();

		var run = () => db.Initializer.InitializeAsync(cancelled.Token);

		await run.Should().ThrowAsync<OperationCanceledException>();
	}
	#endregion
}
