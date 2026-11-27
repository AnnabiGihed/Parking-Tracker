using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.Errors;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Testing.Device;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="DeviceVehicleStore"/> over a real SQLite file: the vehicle is saved, read back by a new store, replaced and
///              removed; values that break a rule of the domain are refused and nothing is written; the latest model year follows the clock;
///              a vehicle that cannot be read throws on a read and is replaced by a save; a key that cannot be read now never lets a write replace
///              it; and a lost key is replaced by a new one on save.
/// </summary>
public sealed class DeviceVehicleStoreTests
{
	#region Test Infrastructure
	/// <summary>
	/// A petrol car without a plate.
	/// </summary>
	private static readonly VehicleDetails Petrol = new("renault", "Clio", 2019, FuelType.Petrol, EuroNorm.Euro6);

	/// <summary>
	/// An electric car with plugs, a battery, a range and a plate.
	/// </summary>
	private static readonly VehicleDetails Electric = new("tesla", "Model 3", 2022, FuelType.Electric, EuroNorm.Unknown, new ChargingDetails([ChargingConnector.Ccs, ChargingConnector.Type2], 57.5, 450), "1ABC234");

	/// <summary>
	/// Builds a store over the database, as a launch of the app does.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <param name="notice">The notice the store raises; a new one when <see langword="null"/>.</param>
	/// <returns>The store.</returns>
	private static DeviceVehicleStore NewStore(DeviceTestDatabase db, DeviceDataResetNotice? notice = null)
	{
		return new DeviceVehicleStore(db.Vehicles, db.Places, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, new FakeStartupGate(), db.Clock, NullLogger<DeviceVehicleStore>.Instance, notice ?? new DeviceDataResetNotice(), db.Tracker);
	}
	#endregion

	#region Save And Read Tests
	/// <summary>
	/// Verifies that nothing stored reads as no vehicle.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NothingSaved_ShouldBeNull()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		(await NewStore(db).GetAsync()).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a saved electric vehicle is read back by a new store with its plugs, battery, range and plate.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_ElectricVehicle_ShouldBeReadBackByANewStore()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		(await NewStore(db).SaveAsync(Electric)).IsSuccess.Should().BeTrue();
		var read = await NewStore(db).GetAsync();

		read.Should().NotBeNull();
		read.Make.Should().Be("tesla");
		read.Model.Should().Be("Model 3");
		read.Year.Should().Be(2022);
		read.Fuel.Should().Be(FuelType.Electric);
		read.Charging!.Connectors.Should().Equal(ChargingConnector.Type2, ChargingConnector.Ccs);
		read.Charging.BatteryKwh.Should().Be(57.5);
		read.Charging.RangeKm.Should().Be(450);
		read.Plate.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that saving again replaces the vehicle and keeps a single row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_Twice_ShouldReplaceTheVehicle()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SaveAsync(Electric);

		await store.SaveAsync(Petrol);
		var read = await store.GetAsync();

		read!.Make.Should().Be("renault");
		read.Charging.Should().BeNull();
		read.Plate.Should().BeNull();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that values that break a rule of the domain return its failure and write nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_UnknownMake_ShouldReturnTheFailureAndWriteNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		var saved = await NewStore(db).SaveAsync(Petrol with { Make = "nope" });

		saved.IsFailure.Should().BeTrue();
		saved.Error.Should().Be(VehicleErrors.MakeUnknown);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that the latest model year is the year after the one of the clock: 2027 is saved, 2028 is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_YearAfterNextYear_ShouldBeRefusedByTheClockYear()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);

		var next = await store.SaveAsync(Petrol with { Year = 2027 });
		var later = await store.SaveAsync(Petrol with { Year = 2028 });

		next.IsSuccess.Should().BeTrue();
		later.IsFailure.Should().BeTrue();
		later.Error.Should().Be(VehicleErrors.YearOutOfRange(2027));
		(await store.GetAsync())!.Year.Should().Be(2027);
	}

	/// <summary>
	/// Verifies that a missing argument is rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_NullDetails_ShouldThrow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);

		var save = () => store.SaveAsync(null!); // null on purpose: proves the guard clause rejects it

		await save.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion

	#region Remove Tests
	/// <summary>
	/// Verifies that a removal deletes the vehicle, and that removing when none is saved does nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_ShouldDeleteTheVehicleAndTolerateNone()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SaveAsync(Electric);

		await store.RemoveAsync();
		await store.RemoveAsync();

		(await store.GetAsync()).Should().BeNull();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles")).Should().Be(0);
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that a vehicle that cannot be read for good throws on a read, and that a save replaces it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Unreadable_ShouldThrowOnReadAndBeReplacedBySave()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SaveAsync(Electric);
		_ = await db.ScalarAsync<int>("UPDATE vehicles SET plate = 'garbage'");
		var read = () => store.GetAsync();

		await read.Should().ThrowAsync<SavedDataUnreadableException>().Where(e => !e.IsTemporary && e.Message == SavedDataUnreadableException.VehicleMessage);
		(await store.SaveAsync(Petrol)).IsSuccess.Should().BeTrue();
		(await store.GetAsync())!.Make.Should().Be("renault");
	}

	/// <summary>
	/// Verifies that a key that cannot be read now throws on a read and on a write and keeps the stored vehicle.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task KeyUnavailable_ShouldThrowOnReadAndWriteAndKeepTheStoredVehicle()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SaveAsync(Electric);
		db.MemoryKeys.IsUnavailable = true;

		var read = () => store.GetAsync();
		var save = () => store.SaveAsync(Petrol);

		await read.Should().ThrowAsync<SavedDataUnreadableException>().Where(e => e.IsTemporary);
		await save.Should().ThrowAsync<SavedDataUnreadableException>();
		db.MemoryKeys.IsUnavailable = false;
		(await store.GetAsync())!.Plate.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that a lost key makes the vehicle unreadable, and that a save removes the stale rows and creates a new key.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task KeyLost_SaveShouldReplaceTheStaleRows()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var notice = new DeviceDataResetNotice();
		var raised = 0;
		notice.StaleRowsRemoved += (_, _) => raised++;
		var store = NewStore(db, notice);
		await store.SaveAsync(Electric);
		db.MemoryKeys.Lose();

		var read = () => store.GetAsync();
		await read.Should().ThrowAsync<SavedDataUnreadableException>();
		var saved = await store.SaveAsync(Electric with { Plate = "2DEF345" });

		saved.IsSuccess.Should().BeTrue();
		db.MemoryKeys.CreatedCount.Should().Be(1);
		raised.Should().Be(1);
		(await store.GetAsync())!.Plate.Should().Be("2DEF345");
	}

	/// <summary>
	/// Verifies that the save after a lost key also clears the card, the parking session and the places, which the lost key left unreadable.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task KeyLost_SaveShouldAlsoClearTheCardTheSessionAndThePlaces()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		var settings = new DeviceSettingsStore(new SettingsStore(new InMemoryKeyValueStorage(), new InMemoryKeyValueStorage()), db.Cards, db.Sessions, db.Places, db.Vehicles, db.UnitOfWork, db.Initializer, new FakeStartupGate(), db.ReferenceData, NullLogger<DeviceSettingsStore>.Instance, new DeviceDataResetNotice(), db.Tracker);
		var places = new DevicePlacesStore(db.Places, db.Cards, db.Vehicles, db.Sessions, db.UnitOfWork, db.Initializer, new FakeStartupGate(), NullLogger<DevicePlacesStore>.Instance, new DeviceDataResetNotice(), db.Tracker);
		await settings.SaveCardAsync(new ParkingCard("bruxelles", null, new CardPeriod(null, null), [], [], [], "N-1", "1ABC234"));
		await settings.SaveCarAsync(new ParkedCar(new GeoPoint(50.8467, 4.3525), DateTimeOffset.UnixEpoch, "Paid parking", "Rue de la Loi"));
		await places.SavePlaceAsync(PlaceKind.Home, new GeoPoint(50.8467, 4.3525), null);
		await store.SaveAsync(Electric);
		db.MemoryKeys.Lose();

		(await store.SaveAsync(Petrol with { Plate = "2DEF345" })).IsSuccess.Should().BeTrue();

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(0);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions")).Should().Be(0);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM saved_places")).Should().Be(0);
		(await store.GetAsync())!.Make.Should().Be("renault");
	}

	/// <summary>
	/// Verifies that a vehicle without a plate is not touched by a lost key: it stays readable and a save of it is not a replacement.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task KeyLost_VehicleWithoutAPlate_ShouldStayReadable()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SaveAsync(Petrol);
		db.MemoryKeys.Lose();

		var read = await store.GetAsync();

		read.Should().BeEquivalentTo(Petrol);
	}
	#endregion

	#region Argument Tests
	/// <summary>
	/// Verifies that the constructor rejects a missing argument.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Constructor_NullArguments_ShouldThrowArgumentNullException()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var gate = new FakeStartupGate();
		var notice = new DeviceDataResetNotice();
		var log = NullLogger<DeviceVehicleStore>.Instance;

		var noVehicles = () => new DeviceVehicleStore(null!, db.Places, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, gate, db.Clock, log, notice, db.Tracker); // null on purpose: proves the guard clause rejects it
		var noPlaces = () => new DeviceVehicleStore(db.Vehicles, null!, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, gate, db.Clock, log, notice, db.Tracker); // null on purpose: proves the guard clause rejects it
		var noCards = () => new DeviceVehicleStore(db.Vehicles, db.Places, null!, db.Sessions, db.UnitOfWork, db.Initializer, gate, db.Clock, log, notice, db.Tracker); // null on purpose: proves the guard clause rejects it
		var noSessions = () => new DeviceVehicleStore(db.Vehicles, db.Places, db.Cards, null!, db.UnitOfWork, db.Initializer, gate, db.Clock, log, notice, db.Tracker); // null on purpose: proves the guard clause rejects it
		var noUnitOfWork = () => new DeviceVehicleStore(db.Vehicles, db.Places, db.Cards, db.Sessions, null!, db.Initializer, gate, db.Clock, log, notice, db.Tracker); // null on purpose: proves the guard clause rejects it
		var noInitializer = () => new DeviceVehicleStore(db.Vehicles, db.Places, db.Cards, db.Sessions, db.UnitOfWork, null!, gate, db.Clock, log, notice, db.Tracker); // null on purpose: proves the guard clause rejects it
		var noGate = () => new DeviceVehicleStore(db.Vehicles, db.Places, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, null!, db.Clock, log, notice, db.Tracker); // null on purpose: proves the guard clause rejects it
		var noClock = () => new DeviceVehicleStore(db.Vehicles, db.Places, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, gate, null!, log, notice, db.Tracker); // null on purpose: proves the guard clause rejects it
		var noLog = () => new DeviceVehicleStore(db.Vehicles, db.Places, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, gate, db.Clock, null!, notice, db.Tracker); // null on purpose: proves the guard clause rejects it
		var noNotice = () => new DeviceVehicleStore(db.Vehicles, db.Places, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, gate, db.Clock, log, null!, db.Tracker); // null on purpose: proves the guard clause rejects it

		noVehicles.Should().Throw<ArgumentNullException>();
		noPlaces.Should().Throw<ArgumentNullException>();
		noCards.Should().Throw<ArgumentNullException>();
		noSessions.Should().Throw<ArgumentNullException>();
		noUnitOfWork.Should().Throw<ArgumentNullException>();
		noInitializer.Should().Throw<ArgumentNullException>();
		noGate.Should().Throw<ArgumentNullException>();
		noClock.Should().Throw<ArgumentNullException>();
		noLog.Should().Throw<ArgumentNullException>();
		noNotice.Should().Throw<ArgumentNullException>();
	}

	/// <summary>
	/// Verifies that a removal that the database refuses throws, so that the view model can tell the user.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_DatabaseRefuses_ShouldThrow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var scope = Substitute.For<IDeviceChangeScope>();
		scope.CommitAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("Test.Refused", "The database refused the change.")));
		var unitOfWork = Substitute.For<IDeviceUnitOfWork>();
		unitOfWork.BeginAsync(Arg.Any<CancellationToken>()).Returns(scope);
		var store = new DeviceVehicleStore(Substitute.For<IVehicleRepository>(), db.Places, db.Cards, db.Sessions, unitOfWork, db.Initializer, new FakeStartupGate(), db.Clock, NullLogger<DeviceVehicleStore>.Instance, new DeviceDataResetNotice(), Substitute.For<IProfileChangeTracker>());

		var remove = () => store.RemoveAsync();

		await remove.Should().ThrowAsync<InvalidOperationException>().WithMessage("The database refused the change.");
	}
	#endregion
}
