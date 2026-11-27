using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Testing.Device;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.Shared.Settings.Database;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="SettingsToDatabaseMigration"/> over a real SQLite file and the real repositories: the card, car
///              and timer move into the database and read back equal, a value that exists already is never overwritten, nothing to move
///              still writes the marker, a failed commit is retried and given up after three attempts, a key that cannot be read now
///              waits without counting an attempt, the move waits for a write of the settings store and never overwrites it, and the
///              old values are never changed or deleted.
/// </summary>
public sealed class SettingsToDatabaseMigrationTests
{
	#region Test Infrastructure
	/// <summary>
	/// Builds the storages with the old values and a migration over the database.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <param name="card">The old <c>pt.card</c>, or <see langword="null"/>.</param>
	/// <param name="car">The old <c>pt.car</c>, or <see langword="null"/>.</param>
	/// <param name="timer">The old <c>pt.timer</c>, or <see langword="null"/>.</param>
	/// <returns>The encrypted storage, the preferences and the migration.</returns>
	private static (InMemoryKeyValueStorage Secure, InMemoryKeyValueStorage Plain, SettingsToDatabaseMigration Migration) Arrange(DeviceTestDatabase db, string? card = null, string? car = null, string? timer = null)
	{
		var secure = new InMemoryKeyValueStorage();
		var plain = new InMemoryKeyValueStorage();
		Seed(secure, SettingsJson.CardKey, card);
		Seed(plain, SettingsJson.CarKey, car);
		Seed(plain, SettingsJson.TimerKey, timer);
		return (secure, plain, NewMigration(db, secure, plain));
	}

	/// <summary>
	/// Saves an old value when there is one.
	/// </summary>
	/// <param name="storage">The storage.</param>
	/// <param name="key">The key.</param>
	/// <param name="value">The value, or <see langword="null"/> for none.</param>
	private static void Seed(InMemoryKeyValueStorage storage, string key, string? value)
	{
		if (value is not null)
		{
			storage.WriteAsync(key, value).GetAwaiter().GetResult();
		}
	}

	/// <summary>
	/// Creates a migration, as a new launch of the app does.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <param name="secure">The encrypted storage.</param>
	/// <param name="plain">The preferences.</param>
	/// <returns>The migration.</returns>
	private static SettingsToDatabaseMigration NewMigration(DeviceTestDatabase db, InMemoryKeyValueStorage secure, InMemoryKeyValueStorage plain)
	{
		return new SettingsToDatabaseMigration(secure, plain, db.Cards, db.Sessions, db.UnitOfWork, db.ReferenceData, db.Clock, NullLogger<SettingsToDatabaseMigration>.Instance);
	}
	#endregion

	#region Constants
	/// <summary>
	/// The old card, as the first version wrote it.
	/// </summary>
	private const string OldCard = "{\"number\":\"UPG-1\",\"plate\":\"1UPG234\",\"municipality\":\"bruxelles\",\"sector\":\"louise\",\"from\":\"2026-01-01\",\"to\":\"2099-12-31\",\"tolerances\":[\"BXL_LIM_IXL_LOUISE\"],\"mailles\":[],\"lims\":[]}";

	/// <summary>
	/// The old parked car: 2026-11-18 10:00 UTC in Brussels.
	/// </summary>
	private const string OldCar = "{\"lat\":50.8467,\"lon\":4.3525,\"t\":1794996000000,\"status\":\"Paid parking\",\"where\":\"Rue de la Loi\"}";

	/// <summary>
	/// The old timer: ends two hours after the parked car.
	/// </summary>
	private const string OldTimer = "{\"end\":1795003200000,\"minutes\":120}";

	/// <summary>
	/// A trigger that makes the database refuse every parking session, so that a commit fails after the card was staged.
	/// </summary>
	private const string RefuseSessions = "CREATE TRIGGER refuse_sessions BEFORE INSERT ON parking_sessions BEGIN SELECT RAISE(ABORT, 'refused'); END";
	#endregion

	#region Migration Tests
	/// <summary>
	/// Verifies that the card, the car and the timer move into the database, read back equal, the marker names what moved, and the old
	/// keys are still there unchanged.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_OldCardCarAndTimer_ShouldMoveThemAndKeepTheOldKeys()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (secure, plain, migration) = Arrange(db, card: OldCard, car: OldCar, timer: OldTimer);

		var outcome = await migration.RunOnceAsync();

		outcome.Should().Be(DatabaseMigrationOutcome.Migrated);
		var card = (await db.Cards.GetAsync()).Value.ToParkingCard();
		card.Plate.Should().Be("1UPG234");
		card.Number.Should().Be("UPG-1");
		card.Sector.Should().Be("louise");
		var session = (await db.Sessions.GetAsync()).Value;
		SettingsJson.SerializeCar(session.ToParkedCar()).Should().Be(SettingsJson.SerializeCar(SettingsJson.ParseCar(OldCar)!));
		session.Timer!.Minutes.Should().Be(120);
		var marker = System.Text.Json.JsonDocument.Parse((await migration.ReadMarkerAsync())!).RootElement;
		marker.GetProperty("outcome").GetString().Should().Be("migrated");
		marker.GetProperty("found").EnumerateArray().Select(element => element.GetString()).Should().BeEquivalentTo("pt.card", "pt.car", "pt.timer");
		(await secure.ReadAsync(SettingsJson.CardKey)).Should().Be(OldCard);
		(await plain.ReadAsync(SettingsJson.CarKey)).Should().Be(OldCar);
		(await plain.ReadAsync(SettingsJson.TimerKey)).Should().Be(OldTimer);
	}

	/// <summary>
	/// Verifies that nothing to move still writes the marker with the outcome <c>nothing</c>, so that the next launch does not look again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_NothingToMove_ShouldWriteTheMarkerAndStoreNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (_, _, migration) = Arrange(db);

		var outcome = await migration.RunOnceAsync();

		outcome.Should().Be(DatabaseMigrationOutcome.NothingFound);
		(await migration.ReadMarkerAsync()).Should().Contain("\"outcome\":\"nothing\"");
		(await db.ScalarAsync<int>("SELECT (SELECT COUNT(*) FROM resident_cards) + (SELECT COUNT(*) FROM parking_sessions)")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that an existing marker stops the run before anything is read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_MarkerExists_ShouldDoNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (_, plain, migration) = Arrange(db, card: OldCard);
		await plain.WriteAsync(SettingsToDatabaseMigration.MarkerKey, "{}");

		var outcome = await migration.RunOnceAsync();

		outcome.Should().Be(DatabaseMigrationOutcome.AlreadyDone);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that every caller of one process gets the same run.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_CalledTwice_ShouldShareOneRun()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (_, _, migration) = Arrange(db, card: OldCard);

		var first = migration.RunOnceAsync();
		var second = migration.RunOnceAsync();

		second.Should().BeSameAs(first);
		(await first).Should().Be(DatabaseMigrationOutcome.Migrated);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that a card or car that the database holds already is never overwritten, and is not listed as moved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_RowsExistAlready_ShouldNotOverwriteThem()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(async scope =>
		{
			var card = await db.Cards.AddAsync(scope, TestData.IxellesCard()).ConfigureAwait(false);
			return card.IsFailure ? card : await db.Sessions.AddAsync(scope, TestData.Session()).ConfigureAwait(false);
		});
		var (_, _, migration) = Arrange(db, card: OldCard, car: OldCar, timer: OldTimer);

		var outcome = await migration.RunOnceAsync();

		outcome.Should().Be(DatabaseMigrationOutcome.NothingFound);
		(await db.Cards.GetAsync()).Value.ToParkingCard().Municipality.Should().Be("ixelles");
		(await db.Sessions.GetAsync()).Value.Headline.Should().Be("Paid parking");
		(await db.Sessions.GetAsync()).Value.Timer.Should().BeNull();
	}

	/// <summary>
	/// Verifies that an old card that does not fit the reference data is skipped and logged, the car still moves, and the old card
	/// stays where it is.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_CardOfUnknownMunicipality_ShouldSkipTheCardAndMoveTheCar()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var unknown = OldCard.Replace("bruxelles", "atlantis", StringComparison.Ordinal);
		var (secure, _, migration) = Arrange(db, card: unknown, car: OldCar);

		var outcome = await migration.RunOnceAsync();

		outcome.Should().Be(DatabaseMigrationOutcome.Migrated);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(0);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions")).Should().Be(1);
		(await secure.ReadAsync(SettingsJson.CardKey)).Should().Be(unknown);
	}

	/// <summary>
	/// Verifies that a timer without a car is not moved, because a session needs a position, and stays in the preferences.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_TimerWithoutCar_ShouldLeaveTheTimerInThePreferences()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (_, plain, migration) = Arrange(db, timer: OldTimer);

		var outcome = await migration.RunOnceAsync();

		outcome.Should().Be(DatabaseMigrationOutcome.NothingFound);
		(await plain.ReadAsync(SettingsJson.TimerKey)).Should().Be(OldTimer);
	}

	/// <summary>
	/// Verifies that a timer that ended before the car was parked is left out of the session but the car still moves.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_TimerBeforeTheCar_ShouldMoveTheCarWithoutTheTimer()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (_, plain, migration) = Arrange(db, car: OldCar, timer: "{\"end\":1000,\"minutes\":30}");

		var outcome = await migration.RunOnceAsync();

		outcome.Should().Be(DatabaseMigrationOutcome.Migrated);
		(await db.Sessions.GetAsync()).Value.Timer.Should().BeNull();
		(await plain.ReadAsync(SettingsJson.TimerKey)).Should().Be("{\"end\":1000,\"minutes\":30}");
		(await migration.ReadMarkerAsync()).Should().NotContain("pt.timer");
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that a commit the database refuses counts as a failed attempt without a marker, rolls back the card that was staged
	/// with the car, and that the next launch moves everything once the database works.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_CommitFails_ShouldCountTheAttemptAndRetryOnTheNextLaunch()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (secure, plain, first) = Arrange(db, card: OldCard, car: OldCar);
		await db.ExecuteAsync(RefuseSessions);

		var failed = await first.RunOnceAsync();

		failed.Should().Be(DatabaseMigrationOutcome.WillRetry);
		(await plain.ReadAsync(SettingsToDatabaseMigration.AttemptsKey)).Should().Be("1");
		(await plain.ReadAsync(SettingsToDatabaseMigration.MarkerKey)).Should().BeNull();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(0);

		await db.ExecuteAsync("DROP TRIGGER refuse_sessions");
		var second = NewMigration(db, secure, plain);
		var retried = await second.RunOnceAsync();

		retried.Should().Be(DatabaseMigrationOutcome.Migrated);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(1);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that the third failed attempt writes the marker with the outcome <c>gaveUp</c>, later launches do nothing, and the old
	/// values are untouched.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_ThreeFailures_ShouldGiveUpAndKeepTheOldValues()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (secure, plain, _) = Arrange(db, card: OldCard, car: OldCar, timer: OldTimer);
		await db.ExecuteAsync(RefuseSessions);

		var outcomes = new List<DatabaseMigrationOutcome>();
		for (var launch = 0; launch < 4; launch++)
		{
			outcomes.Add(await NewMigration(db, secure, plain).RunOnceAsync());
		}

		outcomes.Should().Equal(DatabaseMigrationOutcome.WillRetry, DatabaseMigrationOutcome.WillRetry, DatabaseMigrationOutcome.GaveUp, DatabaseMigrationOutcome.AlreadyDone);
		(await plain.ReadAsync(SettingsToDatabaseMigration.MarkerKey)).Should().Contain("\"outcome\":\"gaveUp\"");
		(await plain.ReadAsync(SettingsToDatabaseMigration.AttemptsKey)).Should().Be("3");
		(await secure.ReadAsync(SettingsJson.CardKey)).Should().Be(OldCard);
		(await plain.ReadAsync(SettingsJson.CarKey)).Should().Be(OldCar);
		(await plain.ReadAsync(SettingsJson.TimerKey)).Should().Be(OldTimer);
	}

	/// <summary>
	/// Verifies that a database that cannot be read counts as a failed attempt and nothing is written.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_DatabaseNotReadable_ShouldCountTheAttempt()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var (_, plain, migration) = Arrange(db, card: OldCard);

		var outcome = await migration.RunOnceAsync();

		outcome.Should().Be(DatabaseMigrationOutcome.WillRetry);
		(await plain.ReadAsync(SettingsToDatabaseMigration.AttemptsKey)).Should().Be("1");
	}
	#endregion

	#region Key And Concurrency Tests
	/// <summary>
	/// Verifies that a key that cannot be read now is not a failed attempt: nothing is counted and no marker is written, so that the
	/// move is not given up for a problem that goes away, and the next launch moves everything.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_KeyUnavailable_ShouldWaitWithoutCountingAnAttempt()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (secure, plain, _) = Arrange(db, card: OldCard, car: OldCar);
		db.MemoryKeys.IsUnavailable = true;

		var outcomes = new List<DatabaseMigrationOutcome>();
		for (var launch = 0; launch < 4; launch++)
		{
			outcomes.Add(await NewMigration(db, secure, plain).RunOnceAsync());
		}

		outcomes.Should().AllBeEquivalentTo(DatabaseMigrationOutcome.WillRetry);
		(await plain.ReadAsync(SettingsToDatabaseMigration.AttemptsKey)).Should().BeNull();
		(await plain.ReadAsync(SettingsToDatabaseMigration.MarkerKey)).Should().BeNull();
		db.MemoryKeys.IsUnavailable = false;
		(await NewMigration(db, secure, plain).RunOnceAsync()).Should().Be(DatabaseMigrationOutcome.Migrated);
	}

	/// <summary>
	/// Verifies that a key that cannot be read now when rows already exist stops the move without a failed attempt, and that the
	/// stored rows are not touched.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_KeyUnavailableWithStoredRows_ShouldNotMistakeThemForNoData()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));
		var (secure, plain, _) = Arrange(db, card: OldCard);
		db.MemoryKeys.IsUnavailable = true;

		var outcome = await NewMigration(db, secure, plain).RunOnceAsync();

		outcome.Should().Be(DatabaseMigrationOutcome.WillRetry);
		(await plain.ReadAsync(SettingsToDatabaseMigration.AttemptsKey)).Should().BeNull();
		(await db.ScalarAsync<string>("SELECT sector FROM resident_cards")).Should().Be("louise");
	}

	/// <summary>
	/// Verifies that the move waits while a write of the settings store holds the scope, and that it then finds the newer card and
	/// does not overwrite it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_StoreWriteInProgress_ShouldWaitForItAndKeepTheNewerCard()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (_, _, migration) = Arrange(db, card: OldCard);
		var writer = await db.UnitOfWork.BeginAsync();
		await using (writer.ConfigureAwait(false))
		{
			var run = migration.RunOnceAsync();
			var finishedEarly = await Task.WhenAny(run, Task.Delay(TimeSpan.FromMilliseconds(250))) == run;
			(await db.Cards.UpdateAsync(writer, TestData.IxellesCard())).IsSuccess.Should().BeTrue();
			(await writer.CommitAsync()).IsSuccess.Should().BeTrue();
			finishedEarly.Should().BeFalse();
			await writer.DisposeAsync();

			(await run).Should().Be(DatabaseMigrationOutcome.NothingFound);
		}

		(await db.ScalarAsync<string>("SELECT municipality FROM resident_cards")).Should().Be("ixelles");
	}

	/// <summary>
	/// Verifies that a save of the settings store that races the move, in whatever order they run, always ends with the newer card.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnce_RacingAStoreWrite_ShouldEndWithTheNewerCard()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (_, plain, migration) = Arrange(db, card: OldCard, car: OldCar);
		var store = new DeviceSettingsStore(new SettingsStore(new InMemoryKeyValueStorage(), plain), db.Cards, db.Sessions, db.Places, db.Vehicles, db.UnitOfWork, db.Initializer, new FakeStartupGate(), db.ReferenceData, NullLogger<DeviceSettingsStore>.Instance, new DeviceDataResetNotice(), db.Tracker);
		var newer = new ParkingCard("ixelles", null, new CardPeriod(null, null), [], ["IXLR_2"], []);

		await Task.WhenAll(migration.RunOnceAsync(), store.SaveCardAsync(newer));

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(1);
		(await db.ScalarAsync<string>("SELECT municipality FROM resident_cards")).Should().Be("ixelles");
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions")).Should().Be(1);
	}
	#endregion

	#region Argument Tests
	/// <summary>
	/// Verifies that the constructor rejects missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Constructor_NullArguments_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var storage = new InMemoryKeyValueStorage();

		var noStorage = () => new SettingsToDatabaseMigration(null!, storage, db.Cards, db.Sessions, db.UnitOfWork, db.ReferenceData, TimeProvider.System, NullLogger<SettingsToDatabaseMigration>.Instance); // null on purpose: proves the guard clause rejects it
		var noLogger = () => new SettingsToDatabaseMigration(storage, storage, db.Cards, db.Sessions, db.UnitOfWork, db.ReferenceData, TimeProvider.System, null!); // null on purpose: proves the guard clause rejects it

		noStorage.Should().Throw<ArgumentNullException>();
		noLogger.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
