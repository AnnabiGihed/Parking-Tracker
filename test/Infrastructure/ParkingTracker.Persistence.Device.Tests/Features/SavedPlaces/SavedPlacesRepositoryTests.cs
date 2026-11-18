using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.Errors;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Persistence.Device.Features.SavedPlaces;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using ParkingTracker.Testing.Device;
using PlaceList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Persistence.Device.Tests.Features.SavedPlaces;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="SavedPlacesRepository"/> against a real SQLite file: the
///              list round-trips in its saved order with the coordinates encrypted at rest, a stored list that breaks a rule gives a
///              failed result, a place that cannot be decrypted, or a key that cannot be read now, fails the read instead of leaving the
///              place out, the list and its places keep their identifiers, the database holds one home and one work place, and an
///              update replaces the whole list.
/// </summary>
public sealed class SavedPlacesRepositoryTests
{
	#region Static Instances
	/// <summary>
	/// A second point inside the Brussels box.
	/// </summary>
	private static readonly GeoPoint Elsewhere = new(50.8333, 4.3667);
	#endregion

	#region Round Trip Tests
	/// <summary>
	/// Verifies that home, work and favourites come back in the order they were saved with their labels, addresses and coordinates,
	/// and that no coordinate is stored as plain text.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_HomeWorkAndFavourites_ShouldRoundTripInOrderWithEncryptedCoordinates()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var places = PlaceList.Create();
		places.SetHome("Home", TestData.GrandPlace, "Grand-Place 1").IsSuccess.Should().BeTrue();
		places.AddFavourite("Gym", Elsewhere, null).IsSuccess.Should().BeTrue();
		places.SetWork("Office", Elsewhere, "Rue de la Loi 16").IsSuccess.Should().BeTrue();

		(await db.ApplyAsync(scope => db.Places.AddAsync(scope, places))).IsSuccess.Should().BeTrue();
		var loaded = await db.Places.GetAsync();

		loaded.IsSuccess.Should().BeTrue();
		loaded.Value.Places.Select(place => (place.Kind, place.Label, place.AddressText, place.Point))
			.Should().Equal(places.Places.Select(place => (place.Kind, place.Label, place.AddressText, place.Point)));
		loaded.Value.Home!.Point.Should().Be(TestData.GrandPlace);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM saved_places WHERE coordinates LIKE '%50.8%'")).Should().Be(0);
		(await db.ScalarAsync<string>("SELECT coordinates FROM saved_places WHERE position = 0")).Should().StartWith("k1.");
	}

	/// <summary>
	/// Verifies that an empty database gives an empty list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NothingStored_ShouldGiveAnEmptyList()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		var loaded = await db.Places.GetAsync();

		loaded.IsSuccess.Should().BeTrue();
		loaded.Value.Places.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that an update replaces the whole stored list, including after the list was loaded and so carries other identifiers.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Update_LoadedList_ShouldReplaceTheStoredList()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var places = PlaceList.Create();
		places.SetHome("Home", TestData.GrandPlace, null);
		places.SetWork("Office", Elsewhere, null);
		await db.ApplyAsync(scope => db.Places.AddAsync(scope, places));
		var loaded = (await db.Places.GetAsync()).Value;
		loaded.SetHome("New home", Elsewhere, "Somewhere").IsSuccess.Should().BeTrue();
		loaded.Remove(loaded.Work!.Id).IsSuccess.Should().BeTrue();

		(await db.ApplyAsync(scope => db.Places.UpdateAsync(scope, loaded))).IsSuccess.Should().BeTrue();
		var reloaded = (await db.Places.GetAsync()).Value;

		reloaded.Places.Should().ContainSingle();
		reloaded.Home!.Label.Should().Be("New home");
		reloaded.Work.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a removal deletes every stored place at commit.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_StoredPlaces_ShouldDeleteThemAtCommit()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var places = PlaceList.Create();
		places.SetHome("Home", TestData.GrandPlace, null);
		await db.ApplyAsync(scope => db.Places.AddAsync(scope, places));

		(await db.ApplyRemovalAsync(db.Places.Remove)).IsSuccess.Should().BeTrue();

		(await db.Places.GetAsync()).Value.Places.Should().BeEmpty();
	}
	#endregion

	#region Invalid Row Tests
	/// <summary>
	/// Verifies that stored places that break a rule of the aggregate (a point outside the Brussels box) give its failed result.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_PointOutsideBrussels_ShouldFailWithTheDomainError()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var coordinates = await db.SealAsync("saved_places", "coordinates", "11111111-1111-1111-1111-111111111111", "48.8566;2.3522");
		await db.ExecuteAsync($"INSERT INTO saved_places (id, list_id, position, kind, label, address_text, coordinates) VALUES ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 0, 0, 'Paris', '', '{coordinates}')");

		var loaded = await db.Places.GetAsync();

		loaded.Error.Should().Be(SavedPlacesErrors.PointOutsideBrussels);
	}

	/// <summary>
	/// Verifies that more than 20 stored favourites are refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_TooManyFavourites_ShouldFailWithTheDomainError()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		for (var index = 0; index <= PlaceList.MaximumFavourites; index++)
		{
			var id = Guid.NewGuid().ToString("D");
			var coordinates = await db.SealAsync("saved_places", "coordinates", id, "50.85;4.35");
			await db.ExecuteAsync($"INSERT INTO saved_places (id, list_id, position, kind, label, address_text, coordinates) VALUES ('{id}', '22222222-2222-2222-2222-222222222222', {index}, 2, 'F{index}', '', '{coordinates}')");
		}

		var loaded = await db.Places.GetAsync();

		loaded.Error.Should().Be(SavedPlacesErrors.TooManyFavourites);
	}

	/// <summary>
	/// Verifies that coordinates that cannot be read are refused as an invalid row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_CoordinatesNotNumbers_ShouldFailWithInvalidRow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var coordinates = await db.SealAsync("saved_places", "coordinates", "11111111-1111-1111-1111-111111111111", "not coordinates");
		await db.ExecuteAsync($"INSERT INTO saved_places (id, list_id, position, kind, label, address_text, coordinates) VALUES ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 0, 0, 'X', '', '{coordinates}')");

		var loaded = await db.Places.GetAsync();

		loaded.IsFailure.Should().BeTrue();
		loaded.Error.Code.Should().Be(DeviceDatabaseErrors.RowInvalid("saved_places", string.Empty).Code);
	}

	/// <summary>
	/// Verifies that a kind that does not exist is refused with the error of the aggregate.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_UnknownKind_ShouldFailWithTheDomainError()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var coordinates = await db.SealAsync("saved_places", "coordinates", "11111111-1111-1111-1111-111111111111", "50.85;4.35");
		await db.ExecuteAsync($"INSERT INTO saved_places (id, list_id, position, kind, label, address_text, coordinates) VALUES ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 0, 7, 'X', '', '{coordinates}')");

		var loaded = await db.Places.GetAsync();

		loaded.Error.Should().Be(SavedPlacesErrors.KindUnknown);
	}

	/// <summary>
	/// Verifies that rows that name different lists are refused as an invalid row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_RowsOfDifferentLists_ShouldFailWithInvalidRow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var places = PlaceList.Create();
		places.SetHome("Home", TestData.GrandPlace, null);
		places.SetWork("Office", Elsewhere, null);
		await db.ApplyAsync(scope => db.Places.AddAsync(scope, places));
		await db.ExecuteAsync("UPDATE saved_places SET list_id = '55555555-5555-5555-5555-555555555555' WHERE position = 1");

		var loaded = await db.Places.GetAsync();

		loaded.Error.Code.Should().Be(DeviceDatabaseErrors.RowInvalid("saved_places", string.Empty).Code);
	}

	/// <summary>
	/// Verifies that a place whose coordinates cannot be decrypted fails the whole read, instead of being left out: the next save of
	/// the list would otherwise erase it for good.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_OnePlaceUndecryptable_ShouldFailTheRead()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var places = PlaceList.Create();
		places.SetHome("Home", TestData.GrandPlace, null);
		places.SetWork("Office", Elsewhere, null);
		await db.ApplyAsync(scope => db.Places.AddAsync(scope, places));
		var foreign = (await new FieldCipher(new InMemoryKeyStore(), db.Probe, NullLogger<FieldCipher>.Instance).EncryptAsync("50.85;4.35", new FieldBinding("saved_places", "coordinates", places.Work!.Id.Value.ToString("D")))).Value;
		await db.ExecuteAsync($"UPDATE saved_places SET coordinates = '{foreign}' WHERE position = 1");

		var loaded = await db.Places.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}

	/// <summary>
	/// Verifies that the coordinates of two places cannot be swapped.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_CoordinatesSwappedBetweenRows_ShouldFailAuthentication()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var places = PlaceList.Create();
		places.SetHome("Home", TestData.GrandPlace, null);
		places.SetWork("Office", Elsewhere, null);
		await db.ApplyAsync(scope => db.Places.AddAsync(scope, places));
		await db.ExecuteAsync("UPDATE saved_places SET coordinates = (SELECT coordinates FROM saved_places WHERE position = 1) WHERE position = 0");

		var loaded = await db.Places.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}

	/// <summary>
	/// Verifies that a key that cannot be read now fails the read with <see cref="DeviceDataErrors.KeyUnavailable"/> and never gives
	/// an empty or a shortened list, and that the list reads again once the key is back.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_KeyUnavailable_ShouldFailWithKeyUnavailableAndRecoverLater()
	{
		var keys = new InMemoryKeyStore();
		await using var db = await DeviceTestDatabase.CreateAsync(keys);
		var places = PlaceList.Create();
		places.SetHome("Home", TestData.GrandPlace, null);
		await db.ApplyAsync(scope => db.Places.AddAsync(scope, places));
		keys.IsUnavailable = true;

		var unavailable = await db.Places.GetAsync();
		keys.IsUnavailable = false;
		var back = await db.Places.GetAsync();

		DeviceDataErrors.IsKeyUnavailable(unavailable).Should().BeTrue();
		back.Value.Home.Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that the list and its places keep their identifiers through a save and a load.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_Places_ShouldKeepTheIdentifiers()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var places = PlaceList.Create();
		places.SetHome("Home", TestData.GrandPlace, null);
		places.AddFavourite("Gym", Elsewhere, null);
		await db.ApplyAsync(scope => db.Places.AddAsync(scope, places));

		var loaded = await db.Places.GetAsync();

		loaded.Value.Id.Should().Be(places.Id);
		loaded.Value.Places.Select(place => place.Id).Should().Equal(places.Places.Select(place => place.Id));
	}

	/// <summary>
	/// Verifies that an add does nothing when places are stored already, so that newer places are never overwritten, and that the
	/// database refuses a second home.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_PlacesStoredAlready_ShouldKeepThemAndTheDatabaseHoldsOneHome()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var first = PlaceList.Create();
		first.SetHome("Home", TestData.GrandPlace, null);
		await db.ApplyAsync(scope => db.Places.AddAsync(scope, first));
		var other = PlaceList.Create();
		other.SetHome("Other home", Elsewhere, null);

		(await db.ApplyAsync(scope => db.Places.AddAsync(scope, other))).IsSuccess.Should().BeTrue();
		var secondHome = () => db.ExecuteAsync("INSERT INTO saved_places (id, list_id, position, kind, label, address_text, coordinates) VALUES ('33333333-3333-3333-3333-333333333333', 'x', 5, 0, 'Second', '', 'x')");

		(await db.Places.GetAsync()).Value.Home!.Label.Should().Be("Home");
		await secondHome.Should().ThrowAsync<SQLite.SQLiteException>();
	}

	/// <summary>
	/// Verifies that a database without the tables gives a failed read, not an exception.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NoTable_ShouldFailWithReadFailed()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var loaded = await db.Places.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.ReadFailed);
	}

	/// <summary>
	/// Verifies that the methods reject missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Members_NullArguments_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var scope = await db.UnitOfWork.BeginAsync();
		await using (scope.ConfigureAwait(false))
		{
			var noAdd = () => db.Places.AddAsync(scope, null!); // null on purpose: proves the guard clause rejects it
			var noUpdate = () => db.Places.UpdateAsync(scope, null!); // null on purpose: proves the guard clause rejects it
			var noScope = () => db.Places.AddAsync(null!, PlaceList.Create()); // null on purpose: proves the guard clause rejects it

			await noAdd.Should().ThrowAsync<ArgumentNullException>();
			await noUpdate.Should().ThrowAsync<ArgumentNullException>();
			await noScope.Should().ThrowAsync<ArgumentNullException>();
		}
	}
	#endregion
}
