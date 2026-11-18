using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Testing.Device;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.Shared.Settings.Database;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="StartupGate"/> over a real SQLite file: after the copy of the previous version's data the schema
///              is created and the old card, car and timer move into the database in one shared run; a schema that cannot be created
///              skips the move without throwing.
/// </summary>
public sealed class StartupGateTests
{
	#region Test Infrastructure
	/// <summary>
	/// Builds a gate whose legacy migration has finished already, over the given initializer.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <param name="initializer">The initializer the gate runs.</param>
	/// <returns>The gate, the encrypted storage and the preferences.</returns>
	private static (StartupGate Gate, InMemoryKeyValueStorage Secure, InMemoryKeyValueStorage Plain) Arrange(DeviceTestDatabase db, IDeviceDatabaseInitializer initializer)
	{
		var secure = new InMemoryKeyValueStorage();
		var plain = new InMemoryKeyValueStorage();
		plain.WriteAsync(LegacyDataMigration.MarkerKey, "{}").GetAwaiter().GetResult();
		var legacy = new LegacyDataMigration(Substitute.For<ILegacyStorageReader>(), Substitute.For<ISettingsStore>(), plain, db.Clock, NullLogger<LegacyDataMigration>.Instance);
		var migration = new SettingsToDatabaseMigration(secure, plain, db.Cards, db.Sessions, db.UnitOfWork, db.ReferenceData, db.Clock, NullLogger<SettingsToDatabaseMigration>.Instance);
		return (new StartupGate(legacy, initializer, migration, NullLogger<StartupGate>.Instance), secure, plain);
	}
	#endregion

	#region Gate Tests
	/// <summary>
	/// Verifies that one run creates the schema and moves the old card and car, and that a second call shares that run.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_FreshDatabaseAndOldValues_ShouldCreateTheSchemaAndMoveTheValues()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var (gate, secure, plain) = Arrange(db, db.Initializer);
		await secure.WriteAsync(SettingsJson.CardKey, "{\"number\":\"A\",\"plate\":\"1ABC234\",\"municipality\":\"bruxelles\",\"sector\":\"louise\",\"from\":\"2026-01-01\",\"to\":\"2099-12-31\",\"tolerances\":[],\"mailles\":[],\"lims\":[]}");
		await plain.WriteAsync(SettingsJson.CarKey, SettingsJson.SerializeCar(new ParkedCar(TestData.GrandPlace, TestData.Parked, "Paid parking", string.Empty)));

		var first = gate.RunOnceAsync();
		var second = gate.RunOnceAsync();
		await first;

		second.Should().BeSameAs(first);
		(await db.Cards.GetAsync()).Value.ToParkingCard().Plate.Should().Be("1ABC234");
		(await db.Sessions.GetAsync()).IsSuccess.Should().BeTrue();
		(await plain.ReadAsync(SettingsToDatabaseMigration.MarkerKey)).Should().Contain("migrated");
	}

	/// <summary>
	/// Verifies that the gate says whether the one-time move has finished: not before the run, and after it once the marker is written.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task IsMoveFinished_BeforeAndAfterTheRun_ShouldFollowTheMarker()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var (gate, _, _) = Arrange(db, db.Initializer);

		var before = await gate.IsMoveFinishedAsync();
		await gate.RunOnceAsync();
		var after = await gate.IsMoveFinishedAsync();

		before.Should().BeFalse();
		after.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a schema that cannot be created skips the move, does not throw and writes no marker, so that the next launch tries again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_SchemaNotReady_ShouldSkipTheMoveWithoutThrowing()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var initializer = Substitute.For<IDeviceDatabaseInitializer>();
		initializer.InitializeAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(DeviceDatabaseErrors.SchemaNewerThanApp));
		var (gate, _, plain) = Arrange(db, initializer);

		var run = () => gate.RunOnceAsync();

		await run.Should().NotThrowAsync();
		(await plain.ReadAsync(SettingsToDatabaseMigration.MarkerKey)).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a copy of the previous version's data that throws does not stop the gate and that the gate still never throws.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_LegacyMigrationThrows_ShouldNotThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var plain = new InMemoryKeyValueStorage();
		var reader = Substitute.For<ILegacyStorageReader>();
		reader.ReadAsync(Arg.Any<CancellationToken>()).Returns<Task<LegacyStorageReading>>(_ => throw new InvalidOperationException("web view gone"));
		var legacy = new LegacyDataMigration(reader, Substitute.For<ISettingsStore>(), plain, db.Clock, NullLogger<LegacyDataMigration>.Instance);
		var migration = new SettingsToDatabaseMigration(new InMemoryKeyValueStorage(), plain, db.Cards, db.Sessions, db.UnitOfWork, db.ReferenceData, db.Clock, NullLogger<SettingsToDatabaseMigration>.Instance);
		var gate = new StartupGate(legacy, db.Initializer, migration, NullLogger<StartupGate>.Instance);

		var run = () => gate.RunOnceAsync();

		await run.Should().NotThrowAsync();
	}

	/// <summary>
	/// Verifies that an initializer that throws does not stop the gate.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_InitializerThrows_ShouldNotThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var initializer = Substitute.For<IDeviceDatabaseInitializer>();
		initializer.InitializeAsync(Arg.Any<CancellationToken>()).Returns<Task<Result>>(_ => throw new InvalidOperationException("disk full"));
		var (gate, _, _) = Arrange(db, initializer);

		var run = () => gate.RunOnceAsync();

		await run.Should().NotThrowAsync();
	}

	/// <summary>
	/// Verifies that the constructor rejects missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Constructor_NullArguments_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var (gate, _, _) = Arrange(db, db.Initializer);
		_ = gate;

		var noLegacy = () => new StartupGate(null!, db.Initializer, null!, NullLogger<StartupGate>.Instance); // null on purpose: proves the guard clause rejects it

		noLegacy.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
