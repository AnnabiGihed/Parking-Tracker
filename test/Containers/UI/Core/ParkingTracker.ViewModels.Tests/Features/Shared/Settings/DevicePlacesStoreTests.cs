using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.Errors;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Testing.Device;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;
using SavedPlacesList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="DevicePlacesStore"/> over a real SQLite file: Home, Work and favourites are saved, read back by a new store,
///              replaced and cleared one by one; a place outside Brussels is refused and nothing is written; saved places that cannot be
///              read throw on a read and are replaced by a save; a key that cannot be read now never lets a write replace them; and a
///              lost key is replaced by a new one on save.
/// </summary>
public sealed class DevicePlacesStoreTests
{
	#region Test Infrastructure
	/// <summary>
	/// A point on the Grand-Place.
	/// </summary>
	private static readonly GeoPoint GrandPlace = new(50.8467, 4.3525);

	/// <summary>
	/// A point in Ixelles.
	/// </summary>
	private static readonly GeoPoint Ixelles = new(50.8250, 4.3606);

	/// <summary>
	/// Builds a store over the database, as a launch of the app does.
	/// </summary>
	/// <param name="db">The database.</param>
	/// <param name="notice">The notice the store raises; a new one when <see langword="null"/>.</param>
	/// <returns>The store.</returns>
	private static DevicePlacesStore NewStore(DeviceTestDatabase db, DeviceDataResetNotice? notice = null)
	{
		return new DevicePlacesStore(db.Places, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, new FakeStartupGate(), NullLogger<DevicePlacesStore>.Instance, notice ?? new DeviceDataResetNotice());
	}
	#endregion

	#region Save And Read Tests
	/// <summary>
	/// Verifies that Home and Work are saved and read back by a new store in the order home, work.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_HomeAndWork_ShouldBeReadBackByANewStore()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);

		(await store.SavePlaceAsync(PlaceKind.Work, Ixelles, " Rue Neuve 5 ")).IsSuccess.Should().BeTrue();
		(await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null)).IsSuccess.Should().BeTrue();
		var places = await NewStore(db).GetPlacesAsync();

		places.Should().HaveCount(2);
		places[0].Should().BeEquivalentTo(new { Kind = PlaceKind.Home, Label = "Home", Point = GrandPlace, AddressText = string.Empty });
		places[1].Should().BeEquivalentTo(new { Kind = PlaceKind.Work, Label = "Work", Point = Ixelles, AddressText = "Rue Neuve 5" });
	}

	/// <summary>
	/// Verifies that nothing stored reads as an empty list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NothingSaved_ShouldBeEmpty()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		(await NewStore(db).GetPlacesAsync()).Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that saving Home again replaces it and keeps Work.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_HomeTwice_ShouldReplaceItAndKeepWork()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, "first");
		await store.SavePlaceAsync(PlaceKind.Work, Ixelles, null);

		await store.SavePlaceAsync(PlaceKind.Home, Ixelles, "second");
		var places = await store.GetPlacesAsync();

		places.Should().HaveCount(2);
		places[0].Point.Should().Be(Ixelles);
		places[0].AddressText.Should().Be("second");
		places[1].Kind.Should().Be(PlaceKind.Work);
	}

	/// <summary>
	/// Verifies that a point outside Brussels returns the failure of the domain and writes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_OutsideBrussels_ShouldReturnTheFailureAndWriteNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);

		var saved = await store.SavePlaceAsync(PlaceKind.Home, new GeoPoint(48.8566, 2.3522), null);

		saved.IsFailure.Should().BeTrue();
		saved.Error.Should().Be(SavedPlacesErrors.PointOutsideBrussels);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM saved_places")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a favourite is refused by both writes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Write_Favourite_ShouldThrow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);

		var save = () => store.SavePlaceAsync(PlaceKind.Favourite, GrandPlace, null);
		var clear = () => store.ClearPlaceAsync(PlaceKind.Favourite);

		await save.Should().ThrowAsync<ArgumentOutOfRangeException>();
		await clear.Should().ThrowAsync<ArgumentOutOfRangeException>();
	}
	#endregion

	#region Clear Tests
	/// <summary>
	/// Verifies that clearing one place keeps the other, and clearing the last removes every row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Clear_OneThenTheLast_ShouldKeepTheOtherThenRemoveEveryRow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);
		await store.SavePlaceAsync(PlaceKind.Work, Ixelles, null);

		await store.ClearPlaceAsync(PlaceKind.Home);
		var remaining = await store.GetPlacesAsync();
		await store.ClearPlaceAsync(PlaceKind.Work);

		remaining.Should().ContainSingle().Which.Kind.Should().Be(PlaceKind.Work);
		(await store.GetPlacesAsync()).Should().BeEmpty();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM saved_places")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that clearing a place that is not saved changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Clear_NotSaved_ShouldDoNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);

		await store.ClearPlaceAsync(PlaceKind.Work);

		(await store.GetPlacesAsync()).Should().ContainSingle();
	}
	#endregion

	#region Favourite Tests
	/// <summary>
	/// Verifies that favourites are saved, read back after Home and Work in the order they were added, and carry their identifiers.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddFavourite_ShouldBeReadBackAfterHomeAndWork()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.AddFavouriteAsync("Bakery", GrandPlace, " Rue 1 ");
		await store.SavePlaceAsync(PlaceKind.Work, Ixelles, null);
		await store.AddFavouriteAsync("Park", Ixelles, null);
		await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);

		var places = await NewStore(db).GetPlacesAsync();

		places.Select(place => place.Label).Should().Equal("Home", "Work", "Bakery", "Park");
		places[2].Kind.Should().Be(PlaceKind.Favourite);
		places[2].AddressText.Should().Be("Rue 1");
		places[2].Id.Should().NotBe(places[3].Id);
	}

	/// <summary>
	/// Verifies that an update keeps the identifier and the position, and changes the name and the place.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task UpdateFavourite_ShouldKeepIdentifierAndPosition()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.AddFavouriteAsync("Bakery", GrandPlace, null);
		await store.AddFavouriteAsync("Park", GrandPlace, null);
		var first = (await store.GetPlacesAsync())[0];

		var updated = await store.UpdateFavouriteAsync(first.Id, "Baker", Ixelles, "Rue 2");
		var places = await store.GetPlacesAsync();

		updated.IsSuccess.Should().BeTrue();
		places.Select(place => place.Label).Should().Equal("Baker", "Park");
		places[0].Id.Should().Be(first.Id);
		places[0].Point.Should().Be(Ixelles);
		places[0].AddressText.Should().Be("Rue 2");
	}

	/// <summary>
	/// Verifies that an update of a favourite that is gone, or with a point outside Brussels, fails and writes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task UpdateFavourite_UnknownOrOutsideBrussels_ShouldReturnTheFailure()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.AddFavouriteAsync("Bakery", GrandPlace, null);
		var first = (await store.GetPlacesAsync())[0];

		var unknown = await store.UpdateFavouriteAsync(SavedPlaceId.New(), "X", Ixelles, null);
		var outside = await store.UpdateFavouriteAsync(first.Id, "X", new GeoPoint(48.8566, 2.3522), null);

		unknown.Error.Should().Be(SavedPlacesErrors.PlaceNotFound);
		outside.Error.Should().Be(SavedPlacesErrors.PointOutsideBrussels);
		(await store.GetPlacesAsync()).Single().Should().BeEquivalentTo(first);
	}

	/// <summary>
	/// Verifies that removing a favourite keeps the others, that removing the last one empties the table, and that an unknown one changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RemoveFavourite_ShouldKeepTheOthersAndIgnoreAnUnknownOne()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.AddFavouriteAsync("Bakery", GrandPlace, null);
		await store.AddFavouriteAsync("Park", Ixelles, null);
		var all = await store.GetPlacesAsync();

		await store.RemoveFavouriteAsync(SavedPlaceId.New());
		await store.RemoveFavouriteAsync(all[0].Id);
		var remaining = await store.GetPlacesAsync();
		await store.RemoveFavouriteAsync(all[1].Id);

		remaining.Should().ContainSingle().Which.Label.Should().Be("Park");
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM saved_places")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that Home and Work cannot be removed as favourites.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RemoveFavourite_HomeIdentifier_ShouldKeepHome()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);
		var home = (await store.GetPlacesAsync())[0];

		await store.RemoveFavouriteAsync(home.Id);

		(await store.GetPlacesAsync()).Should().ContainSingle();
	}

	/// <summary>
	/// Verifies that the 21st favourite is refused and nothing is written for it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddFavourite_TwentyOne_ShouldReturnTheFailureAndWriteNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		for (var index = 1; index <= SavedPlacesList.MaximumFavourites; index++)
		{
			(await store.AddFavouriteAsync("Place " + index, GrandPlace, null)).IsSuccess.Should().BeTrue();
		}

		var twentyFirst = await store.AddFavouriteAsync("One too many", GrandPlace, null);

		twentyFirst.Error.Should().Be(SavedPlacesErrors.TooManyFavourites);
		twentyFirst.ResultExceptionType.Should().Be(ResultExceptionType.Conflict);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM saved_places")).Should().Be(SavedPlacesList.MaximumFavourites);
	}

	/// <summary>
	/// Verifies that a blank name, a too long name and a point outside Brussels are refused and nothing is written.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddFavourite_BlankTooLongOrOutsideBrussels_ShouldReturnTheFailure()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);

		var blank = await store.AddFavouriteAsync(" ", GrandPlace, null);
		var tooLong = await store.AddFavouriteAsync(new string('x', SavedPlace.MaximumLabelLength + 1), GrandPlace, null);
		var outside = await store.AddFavouriteAsync("Paris", new GeoPoint(48.8566, 2.3522), null);

		outside.Error.Should().Be(SavedPlacesErrors.PointOutsideBrussels);
		blank.Error.Should().Be(SavedPlacesErrors.LabelRequired);
		tooLong.Error.Should().Be(SavedPlacesErrors.LabelTooLong);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM saved_places")).Should().Be(0);
	}
	#endregion

	#region Unreadable Data Tests
	/// <summary>
	/// Verifies that stored places that break a rule throw on a read, and that a save replaces them.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_StoredPlacesBreakingARule_ShouldThrowAndASaveShouldReplaceThem()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);
		await db.ExecuteAsync("UPDATE saved_places SET label = ''");

		var read = () => store.GetPlacesAsync();

		await read.Should().ThrowAsync<SavedDataUnreadableException>().Where(e => !e.IsTemporary && e.Message == SavedDataUnreadableException.PlacesMessage);
		(await store.SavePlaceAsync(PlaceKind.Work, Ixelles, null)).IsSuccess.Should().BeTrue();
		(await store.GetPlacesAsync()).Should().ContainSingle().Which.Kind.Should().Be(PlaceKind.Work);
	}

	/// <summary>
	/// Verifies that a key that cannot be read now throws on a read and on a write and keeps the stored place.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task KeyUnavailable_ShouldThrowOnReadAndWriteAndKeepTheStoredPlace()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);
		db.MemoryKeys.IsUnavailable = true;

		var read = () => store.GetPlacesAsync();
		var save = () => store.SavePlaceAsync(PlaceKind.Work, Ixelles, null);
		var clear = () => store.ClearPlaceAsync(PlaceKind.Home);

		await read.Should().ThrowAsync<SavedDataUnreadableException>().Where(e => e.IsTemporary);
		await save.Should().ThrowAsync<SavedDataUnreadableException>();
		await clear.Should().ThrowAsync<SavedDataUnreadableException>();
		db.MemoryKeys.IsUnavailable = false;
		(await store.GetPlacesAsync()).Should().ContainSingle().Which.Point.Should().Be(GrandPlace);
	}

	/// <summary>
	/// Verifies that a lost key makes the places unreadable, and that a save removes the stale rows and creates a new key.
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
		await store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);
		var settings = new DeviceSettingsStore(new SettingsStore(new InMemoryKeyValueStorage(), new InMemoryKeyValueStorage()), db.Cards, db.Sessions, db.Places, db.UnitOfWork, db.Initializer, new FakeStartupGate(), db.ReferenceData, NullLogger<DeviceSettingsStore>.Instance, new DeviceDataResetNotice());
		await settings.SaveCardAsync(new ParkingCard("bruxelles", null, new CardPeriod(null, null), [], [], [], "N-1", "1ABC234"));
		await settings.SaveCarAsync(new ParkedCar(GrandPlace, DateTimeOffset.UnixEpoch, "Paid parking", "Rue de la Loi"));
		db.MemoryKeys.Lose();

		var read = () => store.GetPlacesAsync();
		await read.Should().ThrowAsync<SavedDataUnreadableException>();
		var saved = await store.SavePlaceAsync(PlaceKind.Work, Ixelles, null);

		saved.IsSuccess.Should().BeTrue();
		db.MemoryKeys.CreatedCount.Should().Be(1);
		raised.Should().Be(1);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM resident_cards")).Should().Be(0);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions")).Should().Be(0);
		(await store.GetPlacesAsync()).Should().ContainSingle().Which.Kind.Should().Be(PlaceKind.Work);
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that a commit the database refuses surfaces as an exception.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_CommitRefused_ShouldThrow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = NewStore(db);
		await db.ExecuteAsync("CREATE TRIGGER refuse_places BEFORE INSERT ON saved_places BEGIN SELECT RAISE(ABORT, 'refused'); END");

		var save = () => store.SavePlaceAsync(PlaceKind.Home, GrandPlace, null);

		await save.Should().ThrowAsync<InvalidOperationException>();
	}

	/// <summary>
	/// Verifies that the constructor rejects missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Constructor_NullArguments_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var noPlaces = () => new DevicePlacesStore(null!, db.Cards, db.Sessions, db.UnitOfWork, db.Initializer, new FakeStartupGate(), NullLogger<DevicePlacesStore>.Instance, new DeviceDataResetNotice()); // null on purpose: proves the guard clause rejects it

		noPlaces.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
