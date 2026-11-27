using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Testing.Device;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests that every save or clear of a synced section by the stores stamps that section and sets the changed flag in the same
///              transaction as the data (ADR-0032), over a real SQLite file: home, work, the favourites as one section, the vehicle and the card;
///              that a refused save leaves no stamp; that clearing a place that is not saved changes nothing; and that each commit tells the sync
///              trigger.
/// </summary>
public sealed class ProfileStampingTests
{
	#region Test Infrastructure
	/// <summary>
	/// A point on the Grand-Place.
	/// </summary>
	private static readonly GeoPoint GrandPlace = new(50.8467, 4.3525);

	/// <summary>
	/// Builds the places store over the database.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <returns>The store.</returns>
	private static DevicePlacesStore Places(DeviceTestDatabase db)
	{
		return new DevicePlacesStore(db.Places, db.Cards, db.Vehicles, db.Sessions, db.UnitOfWork, db.Initializer, new FakeStartupGate(), NullLogger<DevicePlacesStore>.Instance, new DeviceDataResetNotice(), db.Tracker);
	}

	/// <summary>
	/// Builds the vehicle store over the database.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <returns>The store.</returns>
	private static DeviceVehicleStore Vehicles(DeviceTestDatabase db)
	{
		return new DeviceVehicleStore(db.Vehicles, db.Places, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, new FakeStartupGate(), db.Clock, NullLogger<DeviceVehicleStore>.Instance, new DeviceDataResetNotice(), db.Tracker);
	}

	/// <summary>
	/// Builds the settings store, which holds the card, over the database.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <returns>The store.</returns>
	private static DeviceSettingsStore Settings(DeviceTestDatabase db)
	{
		return new DeviceSettingsStore(new SettingsStore(new InMemoryKeyValueStorage(), new InMemoryKeyValueStorage()), db.Cards, db.Sessions, db.Places, db.Vehicles, db.UnitOfWork, db.Initializer, new FakeStartupGate(), db.ReferenceData, NullLogger<DeviceSettingsStore>.Instance, new DeviceDataResetNotice(), db.Tracker);
	}

	/// <summary>
	/// Reads the sync state.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <returns>A task with the state.</returns>
	private static async Task<SyncState> StateAsync(DeviceTestDatabase db) => (await db.SyncState.GetAsync().ConfigureAwait(false)).Value;
	#endregion

	#region Place Tests
	/// <summary>
	/// Verifies that saving the home stamps the home only, with the moment of the clock, and sets the flag.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SavePlace_Home_ShouldStampHomeAndSetTheFlag()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		_ = await Places(db).SavePlaceAsync(PlaceKind.Home, GrandPlace, "Grand-Place");
		var state = await StateAsync(db);

		state.IsDirty.Should().BeTrue();
		state.Times.Home.Should().Be(db.Clock.GetUtcNow());
		state.Times.Work.Should().BeNull();
		state.Times.Favourites.Should().BeNull();
	}

	/// <summary>
	/// Verifies that saving the work place, and clearing the home, stamp their own sections.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveWorkAndClearHome_ShouldStampTheirOwnSections()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = Places(db);
		_ = await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);
		db.Clock.Advance(TimeSpan.FromMinutes(1));

		_ = await store.SavePlaceAsync(PlaceKind.Work, GrandPlace, null);
		db.Clock.Advance(TimeSpan.FromMinutes(1));
		await store.ClearPlaceAsync(PlaceKind.Home);
		var state = await StateAsync(db);

		state.Times.Work.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 1, 0, TimeSpan.Zero));
		state.Times.Home.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 2, 0, TimeSpan.Zero));
	}

	/// <summary>
	/// Verifies that adding, updating and removing a favourite stamp the one favourites section.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Favourites_AddUpdateRemove_ShouldStampTheFavouritesSection()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = Places(db);

		_ = await store.AddFavouriteAsync("Gym", GrandPlace, null);
		var afterAdd = (await StateAsync(db)).Times.Favourites;
		db.Clock.Advance(TimeSpan.FromMinutes(1));
		var id = (await store.GetPlacesAsync()).Single().Id;
		_ = await store.UpdateFavouriteAsync(id, "Gym 2", GrandPlace, null);
		var afterUpdate = (await StateAsync(db)).Times.Favourites;
		db.Clock.Advance(TimeSpan.FromMinutes(1));
		await store.RemoveFavouriteAsync(id);
		var afterRemove = (await StateAsync(db)).Times.Favourites;

		afterAdd.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
		afterUpdate.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 1, 0, TimeSpan.Zero));
		afterRemove.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 2, 0, TimeSpan.Zero));
		(await StateAsync(db)).Times.Home.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a refused save leaves no stamp and no flag, so that data and stamp go together.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SavePlace_OutsideBrussels_ShouldLeaveNoStamp()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		var saved = await Places(db).SavePlaceAsync(PlaceKind.Home, new GeoPoint(48.8566, 2.3522), null);

		saved.IsFailure.Should().BeTrue();
		(await StateAsync(db)).Should().Be(SyncState.Empty);
	}

	/// <summary>
	/// Verifies that clearing a place that is not saved changes nothing, stamp included.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ClearPlace_NothingSaved_ShouldLeaveNoStamp()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		await Places(db).ClearPlaceAsync(PlaceKind.Home);

		(await StateAsync(db)).Should().Be(SyncState.Empty);
	}
	#endregion

	#region Vehicle And Card Tests
	/// <summary>
	/// Verifies that saving and removing the vehicle stamp the vehicle section.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Vehicle_SaveAndRemove_ShouldStampTheVehicleSection()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = Vehicles(db);

		_ = await store.SaveAsync(new VehicleDetails("opel", "Corsa", 2015, FuelType.Petrol, EuroNorm.Euro5));
		var afterSave = (await StateAsync(db)).Times.Vehicle;
		db.Clock.Advance(TimeSpan.FromMinutes(1));
		await store.RemoveAsync();
		var afterRemove = (await StateAsync(db)).Times.Vehicle;

		afterSave.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
		afterRemove.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 1, 0, TimeSpan.Zero));
		(await StateAsync(db)).IsDirty.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a vehicle the domain refuses leaves no stamp.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Vehicle_Refused_ShouldLeaveNoStamp()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		var saved = await Vehicles(db).SaveAsync(new VehicleDetails("opel", "Corsa", 1800, FuelType.Petrol, EuroNorm.Euro5));

		saved.IsFailure.Should().BeTrue();
		(await StateAsync(db)).Should().Be(SyncState.Empty);
	}

	/// <summary>
	/// Verifies that saving and clearing the card stamp the card section.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Card_SaveAndClear_ShouldStampTheCardSection()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		using var store = Settings(db);

		await store.SaveCardAsync(new ParkingCard("bruxelles", null, new CardPeriod(null, null), [], [], [], "N-1", "1ABC234"));
		var afterSave = (await StateAsync(db)).Times.Card;
		db.Clock.Advance(TimeSpan.FromMinutes(1));
		await store.ClearCardAsync();
		var afterClear = (await StateAsync(db)).Times.Card;

		afterSave.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
		afterClear.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 1, 0, TimeSpan.Zero));
	}

	/// <summary>
	/// Verifies that the parked car and the timer are never synced: saving them stamps nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ParkedCar_Save_ShouldStampNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		using var store = Settings(db);

		await store.SaveCarAsync(new ParkingTracker.Domain.Features.Cars.ValueObjects.ParkedCar(GrandPlace, TestData.Parked, "Paid parking", "Rue de la Loi"));

		(await StateAsync(db)).Should().Be(SyncState.Empty);
	}
	#endregion

	#region Notification Tests
	/// <summary>
	/// Verifies that each committed change tells the listeners once, and a refused one does not.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Commit_ShouldRaiseChangedOncePerChange()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var raised = 0;
		db.Tracker.Changed += (_, _) => raised++;
		var places = Places(db);

		_ = await places.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);
		_ = await places.SavePlaceAsync(PlaceKind.Work, new GeoPoint(48.8566, 2.3522), null);
		await places.ClearPlaceAsync(PlaceKind.Home);

		raised.Should().Be(2);
	}
	#endregion
}
