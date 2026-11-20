using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Sectors.Services;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Testing.Device;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.Shared.Settings.Database;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="DeviceSettingsStore"/> over a real SQLite file: the card, the parked car and the timer go through
///              the repositories and survive a new store (a new launch), a timer that the session cannot hold stays in the preferences
///              where the first version kept it, the values without invariants stay in the preferences, a database that was never
///              initialized is created on first use, a refused commit surfaces as an exception, as a failed write did before, a key that
///              cannot be read now throws on every read and never reads as "nothing saved" or lets a write overwrite what is stored,
///              and every write waits for the scope that another operation holds.
/// </summary>
public sealed class DeviceSettingsStoreTests
{
	#region Test Infrastructure
	/// <summary>
	/// The moment the car of the tests was parked.
	/// </summary>
	private static readonly DateTimeOffset Parked = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// A car on the Grand-Place.
	/// </summary>
	private static readonly ParkedCar Car = new(new GeoPoint(50.8467, 4.3525), Parked, "Paid parking", "Rue de la Loi");

	/// <summary>
	/// A card of the City of Brussels whose number and plate are encrypted.
	/// </summary>
	private static readonly ParkingCard CityCard = new("bruxelles", null, new CardPeriod(null, null), [], [], [], "N-1", "1ABC234");

	/// <summary>
	/// Builds the real handler of the spot check over a settings store, with the municipalities of the test database.
	/// </summary>
	/// <param name="store">The settings store that holds the card.</param>
	/// <returns>The handler.</returns>
	private static DecideSpotQueryHandler NewSpotHandler(ISettingsStore store)
	{
		var referenceData = Substitute.For<IReferenceData>();
		referenceData.GetReferenceData().Returns(Pivot.Framework.Domain.Shared.Result.Success(new ReferenceDataSet([TestData.City, TestData.Ixelles], [], [])));
		var network = Substitute.For<INetworkStatus>();
		network.IsOnline.Returns(true);
		var contexts = new DecisionContextFactory(store, referenceData, Substitute.For<IOfficialListChecker>());
		return new DecideSpotQueryHandler(contexts, Substitute.For<IDecisionStepRunner>(), network, TimeProvider.System);
	}

	/// <summary>
	/// Builds a store and the preferences that its inner store uses.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <returns>The store and the preferences.</returns>
	private static (DeviceSettingsStore Store, InMemoryKeyValueStorage Plain) Arrange(DeviceTestDatabase db)
	{
		var plain = new InMemoryKeyValueStorage();
		return (NewStore(db, plain), plain);
	}

	/// <summary>
	/// Builds a store over the preferences, as a new launch of the app does.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <param name="plain">The preferences.</param>
	/// <param name="secure">The encrypted storage of the old card; empty when <see langword="null"/>.</param>
	/// <param name="gate">The start-up gate; a finished one when <see langword="null"/>.</param>
	/// <returns>The store.</returns>
	private static DeviceSettingsStore NewStore(DeviceTestDatabase db, InMemoryKeyValueStorage plain, InMemoryKeyValueStorage? secure = null, IStartupGate? gate = null)
	{
		var inner = new SettingsStore(secure ?? new InMemoryKeyValueStorage(), plain);
		return new DeviceSettingsStore(inner, db.Cards, db.Sessions, db.Places, db.Vehicles, db.UnitOfWork, db.Initializer, gate ?? new FakeStartupGate(), db.ReferenceData, NullLogger<DeviceSettingsStore>.Instance, new DeviceDataResetNotice());
	}
	#endregion

	#region Card Tests
	/// <summary>
	/// Verifies that a saved card is read back in the form the aggregate normalises it to, by a new store as well, and that clearing
	/// forgets it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_SaveReadClear_ShouldGoThroughTheDatabase()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		var card = new ParkingCard("bruxelles", "louise", new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2099, 12, 31)), ["BXL_LIM_IXL_LOUISE"], [], [], " TEST ", "1 tst-000");

		await store.SaveCardAsync(card);
		var again = await NewStore(db, plain).GetCardAsync();

		again!.Plate.Should().Be("1TST000");
		again.Number.Should().Be("TEST");
		again.Sector.Should().Be("louise");
		(await store.GetCardAsync())!.Plate.Should().Be("1TST000");
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(1);

		await store.ClearCardAsync();

		(await store.GetCardAsync()).Should().BeNull();
		(await NewStore(db, plain).GetCardAsync()).Should().BeNull();
	}

	/// <summary>
	/// Verifies that saving a second card replaces the first.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_SavedTwice_ShouldKeepOnlyTheLast()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);
		await store.SaveCardAsync(new ParkingCard("bruxelles", null, new CardPeriod(null, null), [], [], []));

		await store.SaveCardAsync(new ParkingCard("ixelles", null, new CardPeriod(null, null), [], ["IXLR_2"], []));

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(1);
		(await store.GetCardAsync())!.Municipality.Should().Be("ixelles");
	}

	/// <summary>
	/// Verifies that a card that breaks a rule of its municipality is refused with an exception and nothing is stored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_BreakingARule_ShouldThrowAndStoreNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);

		var unknown = () => store.SaveCardAsync(new ParkingCard("atlantis", null, new CardPeriod(null, null), [], [], []));
		var reversed = () => store.SaveCardAsync(new ParkingCard("bruxelles", null, new CardPeriod(new DateOnly(2027, 1, 1), new DateOnly(2026, 1, 1)), [], [], []));

		await unknown.Should().ThrowAsync<InvalidOperationException>();
		await reversed.Should().ThrowAsync<InvalidOperationException>();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a stored card that cannot be rebuilt is not "no card": the read throws the explicit unreadable state, and the state
	/// is remembered, so that a repaired row is not read again until the user saves a card.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_StoredRowInvalid_ShouldThrowUnreadableAndRememberIt()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);
		await db.ExecuteAsync("INSERT INTO resident_cards (id, municipality, tolerances, mailles, lims) VALUES ('11111111-1111-1111-1111-111111111111', 'atlantis', '[]', '[]', '[]')");

		var first = () => store.GetCardAsync();
		await first.Should().ThrowAsync<SavedDataUnreadableException>().Where(e => !e.IsTemporary && e.Message == SavedDataUnreadableException.CardMessage);
		await db.ExecuteAsync("UPDATE resident_cards SET municipality = 'bruxelles'");

		await first.Should().ThrowAsync<SavedDataUnreadableException>();
		await store.SaveCardAsync(CityCard);
		(await store.GetCardAsync())!.Municipality.Should().Be("bruxelles");
	}

	/// <summary>
	/// Verifies that a card field that no longer authenticates (moved to another row) is unreadable, with the code that says why.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_FieldRejectedByTheCipher_ShouldThrowUnreadable()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		await store.SaveCardAsync(CityCard);
		var foreign = await db.SealAsync("resident_cards", "plate", "22222222-2222-2222-2222-222222222222", "1XYZ999");
		await db.ExecuteAsync($"UPDATE resident_cards SET plate = '{foreign}'");

		var read = () => NewStore(db, plain).GetCardAsync();

		(await read.Should().ThrowAsync<SavedDataUnreadableException>()).Which.ErrorCode.Should().Be(DeviceDatabaseErrors.CiphertextRejected.Code);
	}
	#endregion

	#region Car And Timer Tests
	/// <summary>
	/// Verifies that a saved car is read back unchanged by a new store, and forgotten by clearing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Car_SaveReadClear_ShouldGoThroughTheDatabase()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);

		await store.SaveCarAsync(Car);
		var again = await NewStore(db, plain).GetCarAsync();

		SettingsJson.SerializeCar(again!).Should().Be(SettingsJson.SerializeCar(Car));
		await store.ClearCarAsync();
		(await store.GetCarAsync()).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a timer of a saved car lives in the session, not in the preferences, and survives a new store.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Timer_WithACar_ShouldLiveInTheSession()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		await store.SaveCarAsync(Car);
		var timer = new ParkingTimer(Parked.AddMinutes(60), 60);

		await store.SaveTimerAsync(timer);

		(await NewStore(db, plain).GetTimerAsync()).Should().Be(timer);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions WHERE timer_minutes = 60")).Should().Be(1);
		(await plain.ReadAsync(SettingsJson.TimerKey)).Should().BeNull();

		await store.ClearTimerAsync();

		(await store.GetTimerAsync()).Should().BeNull();
		(await store.GetCarAsync()).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a timer without a saved car stays in the preferences, as in the first version, and clears from there.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Timer_WithoutACar_ShouldStayInThePreferences()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		var timer = new ParkingTimer(Parked.AddMinutes(60), 60);

		await store.SaveTimerAsync(timer);

		(await store.GetTimerAsync()).Should().Be(timer);
		(await plain.ReadAsync(SettingsJson.TimerKey)).Should().NotBeNull();
		await store.ClearTimerAsync();
		(await store.GetTimerAsync()).Should().BeNull();
		(await plain.ReadAsync(SettingsJson.TimerKey)).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a timer shorter than the form allows, as the test hook starts one, is still saved and read back, from the
	/// preferences, and that the session holds no timer then.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Timer_OneMinuteWithACar_ShouldBeKeptInThePreferences()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		await store.SaveCarAsync(Car);
		var timer = new ParkingTimer(Parked.AddMinutes(1), 1);

		await store.SaveTimerAsync(timer);

		(await NewStore(db, plain).GetTimerAsync()).Should().Be(timer);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions WHERE timer_end IS NOT NULL")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a new timer replaces one the session held, also when the new one has to go to the preferences.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Timer_ReplacedByOneTheSessionCannotHold_ShouldReadTheNewOne()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);
		await store.SaveCarAsync(Car);
		await store.SaveTimerAsync(new ParkingTimer(Parked.AddMinutes(60), 60));
		var short_ = new ParkingTimer(Parked.AddMinutes(2), 2);

		await store.SaveTimerAsync(short_);

		(await store.GetTimerAsync()).Should().Be(short_);
	}

	/// <summary>
	/// Verifies that saving the car again while a timer runs keeps the timer.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Car_SavedAgainWhileATimerRuns_ShouldKeepTheTimer()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);
		var timer = new ParkingTimer(Parked.AddMinutes(120), 120);
		await store.SaveCarAsync(Car);
		await store.SaveTimerAsync(timer);

		await store.SaveCarAsync(Car with { Time = Parked.AddMinutes(5) });

		(await store.GetTimerAsync()).Should().Be(timer);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that a timer that ended before the new car was parked is kept in the preferences instead of being lost or refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Car_SavedAfterTheTimerEnded_ShouldKeepTheTimerInThePreferences()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);
		var timer = new ParkingTimer(Parked.AddMinutes(30), 30);
		await store.SaveCarAsync(Car);
		await store.SaveTimerAsync(timer);

		await store.SaveCarAsync(Car with { Time = Parked.AddHours(2) });

		(await store.GetTimerAsync()).Should().Be(timer);
		(await store.GetCarAsync())!.Time.Should().Be(Parked.AddHours(2));
	}

	/// <summary>
	/// Verifies that forgetting the car leaves the timer running, as the view model stops it as a separate step.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Car_Cleared_ShouldLeaveTheTimer()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);
		var timer = new ParkingTimer(Parked.AddMinutes(60), 60);
		await store.SaveCarAsync(Car);
		await store.SaveTimerAsync(timer);

		await store.ClearCarAsync();

		(await store.GetCarAsync()).Should().BeNull();
		(await store.GetTimerAsync()).Should().Be(timer);
		await store.ClearTimerAsync();
		(await store.GetTimerAsync()).Should().BeNull();
	}
	#endregion

	#region Key And Gate Tests
	/// <summary>
	/// Verifies that a card that cannot be read because the key is unavailable throws instead of reading as "no card", is not
	/// remembered as "no card", and reads again once the key is back.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_KeyUnavailable_ShouldThrowInsteadOfReadingAsNoCard()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		await store.SaveCardAsync(new ParkingCard("bruxelles", null, new CardPeriod(null, null), [], [], [], "N-1", "1ABC234"));
		var fresh = NewStore(db, plain);
		db.MemoryKeys.IsUnavailable = true;

		var read = () => fresh.GetCardAsync();

		await read.Should().ThrowAsync<InvalidOperationException>();
		db.MemoryKeys.IsUnavailable = false;
		(await fresh.GetCardAsync())!.Plate.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that a car, a timer and a save of the car with a key that cannot be read now all throw, and that the stored car and
	/// timer are still there afterwards: a write never replaces what it could not read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Car_KeyUnavailable_ShouldThrowOnEveryCallAndKeepTheStoredCar()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		var timer = new ParkingTimer(Parked.AddMinutes(60), 60);
		await store.SaveCarAsync(Car);
		await store.SaveTimerAsync(timer);
		var fresh = NewStore(db, plain);
		db.MemoryKeys.IsUnavailable = true;

		var getCar = () => fresh.GetCarAsync();
		var getTimer = () => fresh.GetTimerAsync();
		var saveCar = () => fresh.SaveCarAsync(Car with { Time = Parked.AddMinutes(5) });
		var clearTimer = () => fresh.ClearTimerAsync();
		var clearCar = () => fresh.ClearCarAsync();

		await getCar.Should().ThrowAsync<InvalidOperationException>();
		await getTimer.Should().ThrowAsync<InvalidOperationException>();
		await saveCar.Should().ThrowAsync<InvalidOperationException>();
		await clearTimer.Should().ThrowAsync<InvalidOperationException>();
		await clearCar.Should().ThrowAsync<InvalidOperationException>();
		db.MemoryKeys.IsUnavailable = false;
		SettingsJson.SerializeCar((await fresh.GetCarAsync())!).Should().Be(SettingsJson.SerializeCar(Car));
		(await fresh.GetTimerAsync()).Should().Be(timer);
	}

	/// <summary>
	/// Verifies that saving a card with a key that cannot be read now throws and keeps the stored card.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_SavedWhileKeyUnavailable_ShouldThrowAndKeepTheStoredCard()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		await store.SaveCardAsync(new ParkingCard("bruxelles", null, new CardPeriod(null, null), [], [], [], null, "1ABC234"));
		var fresh = NewStore(db, plain);
		db.MemoryKeys.IsUnavailable = true;

		var save = () => fresh.SaveCardAsync(new ParkingCard("ixelles", null, new CardPeriod(null, null), [], [], [], null, "2XYZ345"));

		await save.Should().ThrowAsync<InvalidOperationException>();
		db.MemoryKeys.IsUnavailable = false;
		(await fresh.GetCardAsync())!.Municipality.Should().Be("bruxelles");
	}

	/// <summary>
	/// Verifies that a write waits while another operation holds the scope of the unit of work, and then completes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Write_WhileAnotherScopeIsOpen_ShouldWaitForIt()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);
		var holder = await db.UnitOfWork.BeginAsync();
		Task save;
		bool finishedEarly;
		await using (holder.ConfigureAwait(false))
		{
			save = store.SaveCarAsync(Car);
			finishedEarly = await Task.WhenAny(save, Task.Delay(TimeSpan.FromMilliseconds(250))) == save;
		}

		await save;

		finishedEarly.Should().BeFalse();
		(await store.GetCarAsync()).Should().NotBeNull();
	}
	#endregion

	#region Key Lost Tests
	/// <summary>
	/// Verifies that the reset notice makes the store forget the cached card and the remembered unreadable states, so that the next
	/// read goes to the repository.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ResetNotice_Raised_ShouldForgetTheCachedCardAndTheUnreadableStates()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var notice = new DeviceDataResetNotice();
		var plain = new InMemoryKeyValueStorage();
		using var store = new DeviceSettingsStore(new SettingsStore(new InMemoryKeyValueStorage(), plain), db.Cards, db.Sessions, db.Places, db.Vehicles, db.UnitOfWork, db.Initializer, new FakeStartupGate(), db.ReferenceData, NullLogger<DeviceSettingsStore>.Instance, notice);
		await store.SaveCardAsync(CityCard);
		(await store.GetCardAsync()).Should().NotBeNull();
		await db.ExecuteAsync("DELETE FROM resident_cards");
		(await store.GetCardAsync()).Should().NotBeNull();

		notice.Raise();

		(await store.GetCardAsync()).Should().BeNull();
		notice.Raise();
		await db.ExecuteAsync("INSERT INTO resident_cards (id, municipality, tolerances, mailles, lims) VALUES ('11111111-1111-1111-1111-111111111111', 'atlantis', '[]', '[]', '[]')");
		var unreadable = () => store.GetCardAsync();
		await unreadable.Should().ThrowAsync<SavedDataUnreadableException>();
		await db.ExecuteAsync("UPDATE resident_cards SET municipality = 'bruxelles'");
		await unreadable.Should().ThrowAsync<SavedDataUnreadableException>();

		notice.Raise();

		(await store.GetCardAsync()).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a card whose key was dropped by the protected storage is unreadable (not "no card"), that a car cannot be saved
	/// meanwhile and no key is created, and that entering the card again creates the key, removes the stale rows in the same write and
	/// reads back.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_KeyLost_ShouldBeUnreadableUntilTheCardIsEnteredAgain()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		await store.SaveCardAsync(CityCard);
		await store.SaveCarAsync(Car);
		var launch = NewStore(db, plain);
		var keys = db.MemoryKeys;
		keys.Lose();

		var read = () => launch.GetCardAsync();
		var readCar = () => launch.GetCarAsync();
		var saveCar = () => launch.SaveCarAsync(Car with { Time = Parked.AddMinutes(5) });

		await read.Should().ThrowAsync<SavedDataUnreadableException>().Where(e => e.ErrorCode == DeviceDatabaseErrors.KeyMissing.Code);
		await readCar.Should().ThrowAsync<SavedDataUnreadableException>();
		await saveCar.Should().ThrowAsync<InvalidOperationException>();
		keys.CreatedCount.Should().Be(0);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions")).Should().Be(1);

		await launch.SaveCardAsync(CityCard with { Plate = "2NEW345" });

		keys.CreatedCount.Should().Be(1);
		(await launch.GetCardAsync())!.Plate.Should().Be("2NEW345");
		(await NewStore(db, plain).GetCardAsync())!.Plate.Should().Be("2NEW345");
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(1);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions")).Should().Be(0);
		(await launch.GetCarAsync()).Should().BeNull();
	}

	/// <summary>
	/// Verifies that entering the card again after a lost key clears only the unreadable plate of the vehicle and keeps the row and its
	/// other values, and that a vehicle without a plate is kept as it is.
	/// </summary>
	/// <param name="plate">The plate of the vehicle, or <see langword="null"/> for none.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("1ABC234")]
	[InlineData(null)]
	public async Task Card_KeyLost_SaveShouldKeepTheVehicleWithoutItsPlate(string? plate)
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		await store.SaveCardAsync(CityCard);
		var vehicle = Vehicle.Create(new VehicleDetails("renault", "Clio", 2019, FuelType.Petrol, EuroNorm.Euro6, null, plate), 2026).Value;
		(await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, vehicle))).IsSuccess.Should().BeTrue();
		var launch = NewStore(db, plain);
		db.MemoryKeys.Lose();

		await launch.SaveCardAsync(CityCard with { Plate = "2NEW345" });

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles")).Should().Be(1);
		(await db.Vehicles.GetAsync()).Value.ToDetails().Should().BeEquivalentTo(vehicle.ToDetails() with { Plate = null });
		(await launch.GetCardAsync())!.Plate.Should().Be("2NEW345");
	}

	/// <summary>
	/// Verifies that a phone that has no key and no encrypted row, as after a first install, gets its key when the card is saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_NoKeyAndNoRows_ShouldCreateTheKeyOnSave()
	{
		var keys = InMemoryKeyStore.WithoutKey();
		await using var db = await DeviceTestDatabase.CreateAsync(keys);
		var (store, plain) = Arrange(db);

		await store.SaveCardAsync(CityCard);

		keys.CreatedCount.Should().Be(1);
		(await NewStore(db, plain).GetCardAsync())!.Plate.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that the spot check answers "can't verify" with the message that asks for the card, never FREE, when the saved card
	/// cannot be read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SpotCheck_UnreadableCard_ShouldCantVerifyAndAskForTheCard()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		await store.SaveCardAsync(CityCard);
		(db.MemoryKeys).Lose();
		var handler = NewSpotHandler(NewStore(db, plain));

		var answer = await handler.Handle(new DecideSpotQuery(TestData.GrandPlace.Latitude, TestData.GrandPlace.Longitude, null), CancellationToken.None);

		answer.IsSuccess.Should().BeTrue();
		answer.Value.Verdict.Should().Be(Verdict.CantVerify);
		answer.Value.Status.Should().NotBe(SpotStatus.Free);
		answer.Value.Detail.Should().Be(SavedDataUnreadableException.CardMessage);
	}

	/// <summary>
	/// Verifies that the card view model shows the unreadable card as an explicit state: the line asks for the card, the dialog opens
	/// empty with the same message, and it is not shown as "No card yet".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CardViewModel_UnreadableCard_ShouldShowTheUnreadableLineAndMessage()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);
		await store.SaveCardAsync(CityCard);
		(db.MemoryKeys).Lose();
		using var fixture = new UiFixture();
		var card = new CardViewModel(fixture.Sender, NewStore(db, plain), fixture.ReferenceData, fixture.Launcher, fixture.Clock);

		await card.RefreshLineAsync();
		var line = card.Line;
		await card.OpenAsync();

		line.Pill.Should().NotBe("No card yet");
		line.Detail.Should().Be(SavedDataUnreadableException.CardMessage);
		line.ShowAddLink.Should().BeTrue();
		card.FormError.Should().Be(SavedDataUnreadableException.CardMessage);
		card.Number.Should().BeEmpty();
	}
	#endregion

	#region Old Copy Tests
	/// <summary>
	/// Verifies that clearing the card before the one-time move has finished also removes the old copy, so that the move at the next
	/// launch does not bring the deleted card back; and that the old copy is left alone once the move has finished.
	/// </summary>
	/// <param name="moveFinished">Whether the move has finished.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Card_ClearedBeforeTheMove_ShouldRemoveTheOldCopy(bool moveFinished)
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var secure = new InMemoryKeyValueStorage();
		var plain = new InMemoryKeyValueStorage();
		await secure.WriteAsync(SettingsJson.CardKey, SettingsJson.SerializeCard(CityCard));
		var store = NewStore(db, plain, secure, new FakeStartupGate { MoveFinished = moveFinished });

		await store.ClearCardAsync();

		secure.Values.ContainsKey(SettingsJson.CardKey).Should().Be(moveFinished);
	}

	/// <summary>
	/// Verifies that saving a card before the move has finished also removes the old copy, so that it is not imported later.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_SavedBeforeTheMove_ShouldRemoveTheOldCopy()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var secure = new InMemoryKeyValueStorage();
		var plain = new InMemoryKeyValueStorage();
		await secure.WriteAsync(SettingsJson.CardKey, SettingsJson.SerializeCard(CityCard with { Plate = "1OLD000" }));
		var store = NewStore(db, plain, secure, new FakeStartupGate { MoveFinished = false });

		await store.SaveCardAsync(CityCard);

		secure.Values.Should().NotContainKey(SettingsJson.CardKey);
	}

	/// <summary>
	/// Verifies that clearing or saving the car before the move has finished removes the old copy of the car.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Car_ClearedOrSavedBeforeTheMove_ShouldRemoveTheOldCopy()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var plain = new InMemoryKeyValueStorage();
		var store = NewStore(db, plain, gate: new FakeStartupGate { MoveFinished = false });
		await plain.WriteAsync(SettingsJson.CarKey, SettingsJson.SerializeCar(Car));
		await store.ClearCarAsync();
		var clearedKeepsCopy = plain.Values.ContainsKey(SettingsJson.CarKey);
		await plain.WriteAsync(SettingsJson.CarKey, SettingsJson.SerializeCar(Car));

		await store.SaveCarAsync(Car with { Time = Parked.AddMinutes(5) });

		clearedKeepsCopy.Should().BeFalse();
		plain.Values.Should().NotContainKey(SettingsJson.CarKey);
	}

	/// <summary>
	/// Verifies that the whole path holds together: a card cleared before the move stays cleared when the next launch runs the move.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_ClearedBeforeTheMove_ShouldStayClearedAfterTheNextLaunchMoves()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var secure = new InMemoryKeyValueStorage();
		var plain = new InMemoryKeyValueStorage();
		await secure.WriteAsync(SettingsJson.CardKey, SettingsJson.SerializeCard(CityCard));
		await NewStore(db, plain, secure, new FakeStartupGate { MoveFinished = false }).ClearCardAsync();
		var migration = new SettingsToDatabaseMigration(secure, plain, db.Cards, db.Sessions, db.UnitOfWork, db.ReferenceData, db.Clock, NullLogger<SettingsToDatabaseMigration>.Instance);

		_ = await migration.RunOnceAsync();

		(await NewStore(db, plain, secure).GetCardAsync()).Should().BeNull();
	}

	/// <summary>
	/// Verifies that an old copy that cannot be removed is logged and does not fail a change that is committed already.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_OldCopyCannotBeRemoved_ShouldStillSucceed()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var inner = Substitute.For<ISettingsStore>();
		inner.ClearCardAsync().Returns(Task.FromException(new IOException("storage gone")));
		var store = new DeviceSettingsStore(inner, db.Cards, db.Sessions, db.Places, db.Vehicles, db.UnitOfWork, db.Initializer, new FakeStartupGate { MoveFinished = false }, db.ReferenceData, NullLogger<DeviceSettingsStore>.Instance, new DeviceDataResetNotice());

		await store.SaveCardAsync(CityCard);

		(await store.GetCardAsync())!.Plate.Should().Be("1ABC234");
	}
	#endregion

	#region Gate Tests
	/// <summary>
	/// Verifies that a reader that runs before the start-up gate has finished waits for it and then sees the card that the gate
	/// moved in, instead of caching "no card".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_ReadBeforeTheGateFinished_ShouldWaitForTheGate()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var gate = new FakeStartupGate(finished: false);
		var store = NewStore(db, new InMemoryKeyValueStorage(), gate: gate);
		var read = store.GetCardAsync();

		var finishedEarly = await Task.WhenAny(read, Task.Delay(TimeSpan.FromMilliseconds(250))) == read;
		await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()));
		gate.Finish();
		var card = await read;

		finishedEarly.Should().BeFalse();
		card.Should().NotBeNull();
		(await store.GetCardAsync()).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a write waits for the gate before it takes the scope of the unit of work, which the move of the old values needs:
	/// the scope stays free while the gate runs.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Write_BeforeTheGateFinished_ShouldWaitWithoutHoldingTheScope()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var gate = new FakeStartupGate(finished: false);
		var store = NewStore(db, new InMemoryKeyValueStorage(), gate: gate);
		var save = store.SaveCarAsync(Car);

		var scope = await db.UnitOfWork.BeginAsync().WaitAsync(TimeSpan.FromSeconds(5));
		await scope.DisposeAsync();
		gate.Finish();
		await save;

		(await store.GetCarAsync()).Should().NotBeNull();
	}
	#endregion

	#region Preferences Tests
	/// <summary>
	/// Verifies that the values without invariants stay in the preferences.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OtherValues_ShouldStayInThePreferences()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, plain) = Arrange(db);

		(await store.GetAlertsEnabledAsync()).Should().BeTrue();
		await store.SaveAlertsEnabledAsync(false);
		await store.SaveWelcomeSeenAsync();
		await store.SavePermissionSnoozeUntilAsync(Parked);
		await store.SaveLayersAsync(new ParkingTracker.Domain.Features.MapLayers.ValueObjects.LayerSettings());

		(await store.GetAlertsEnabledAsync()).Should().BeFalse();
		(await store.GetWelcomeSeenAsync()).Should().BeTrue();
		(await store.GetPermissionSnoozeUntilAsync()).Should().Be(Parked);
		(await store.GetLayersAsync()).Should().NotBeNull();
		(await plain.ReadAsync(SettingsJson.AlertsKey)).Should().Be("false");
	}
	#endregion

	#region Robustness Tests
	/// <summary>
	/// Verifies that a database that was never initialized, as in a background process that the start-up gate did not run in, is
	/// created on first use.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Car_SavedInAnUninitializedDatabase_ShouldCreateTheSchemaFirst()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var (store, _) = Arrange(db);

		await store.SaveCarAsync(Car);

		(await store.GetCarAsync()).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a stored session that breaks a rule is not "no car": the read throws the unreadable state, and saving or clearing
	/// the car replaces it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Car_StoredSessionInvalid_ShouldThrowUnreadableUntilTheCarIsSaved()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session()));
		await db.ExecuteAsync($"UPDATE parking_sessions SET timer_end = {Parked.ToUnixTimeMilliseconds()}, timer_minutes = 1");

		var read = () => store.GetCarAsync();
		await read.Should().ThrowAsync<SavedDataUnreadableException>().Where(e => e.Message == SavedDataUnreadableException.CarMessage);
		await store.SaveCarAsync(Car);

		(await store.GetCarAsync()).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a commit the database refuses surfaces as an exception, as a failed write of the preferences did.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Car_CommitRefused_ShouldThrow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var (store, _) = Arrange(db);
		await db.ExecuteAsync("CREATE TRIGGER refuse_sessions BEFORE INSERT ON parking_sessions BEGIN SELECT RAISE(ABORT, 'refused'); END");

		var save = () => store.SaveCarAsync(Car);

		await save.Should().ThrowAsync<InvalidOperationException>();
	}

	/// <summary>
	/// Verifies that the constructor and the methods reject missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Members_NullArguments_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var (store, _) = Arrange(db);

		var noInner = () => new DeviceSettingsStore(null!, db.Cards, db.Sessions, db.Places, db.Vehicles, db.UnitOfWork, db.Initializer, new FakeStartupGate(), db.ReferenceData, NullLogger<DeviceSettingsStore>.Instance, new DeviceDataResetNotice()); // null on purpose: proves the guard clause rejects it
		var noCard = () => store.SaveCardAsync(null!); // null on purpose: proves the guard clause rejects it
		var noCar = () => store.SaveCarAsync(null!); // null on purpose: proves the guard clause rejects it
		var noTimer = () => store.SaveTimerAsync(null!); // null on purpose: proves the guard clause rejects it

		var noGate = () => new DeviceSettingsStore(new SettingsStore(new InMemoryKeyValueStorage(), new InMemoryKeyValueStorage()), db.Cards, db.Sessions, db.Places, db.Vehicles, db.UnitOfWork, db.Initializer, null!, db.ReferenceData, NullLogger<DeviceSettingsStore>.Instance, new DeviceDataResetNotice()); // null on purpose: proves the guard clause rejects it

		noInner.Should().Throw<ArgumentNullException>();
		noGate.Should().Throw<ArgumentNullException>();
		await noCard.Should().ThrowAsync<ArgumentNullException>();
		await noCar.Should().ThrowAsync<ArgumentNullException>();
		await noTimer.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion
}
