using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Testing.Device;
using PlaceList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Persistence.Device.Tests.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="DeviceDataWiper"/> against a real SQLite file: the profile wipe removes the places, the vehicle, the
///              card and the sync state and keeps the parking session and the owner, the full wipe removes the session and the owner too,
///              a wipe of an empty database succeeds, and a commit that fails removes nothing.
/// </summary>
public sealed class DeviceDataWiperTests
{
	#region Test Infrastructure
	/// <summary>
	/// Fills every table that a wipe touches.
	/// </summary>
	/// <param name="db">The database of the test.</param>
	/// <returns>A task that completes when the rows are committed.</returns>
	private static async Task FillAsync(DeviceTestDatabase db)
	{
		var places = PlaceList.Create();
		places.SetHome("Home", TestData.GrandPlace, "Grand-Place 1").IsSuccess.Should().BeTrue();
		(await db.ApplyAsync(scope => db.Places.AddAsync(scope, places)).ConfigureAwait(false)).IsSuccess.Should().BeTrue();
		(await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.PetrolVehicle())).ConfigureAwait(false)).IsSuccess.Should().BeTrue();
		(await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard())).ConfigureAwait(false)).IsSuccess.Should().BeTrue();
		(await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session())).ConfigureAwait(false)).IsSuccess.Should().BeTrue();
		await db.ExecuteAsync("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (1, 'abc', 1)").ConfigureAwait(false);
		await db.ExecuteAsync("INSERT INTO sync_state (id, base_revision, dirty, home_updated_at) VALUES (1, 4, 1, 100)").ConfigureAwait(false);
	}

	/// <summary>
	/// Builds the wiper over the repositories of the database of a test.
	/// </summary>
	/// <param name="db">The database of the test.</param>
	/// <returns>The wiper.</returns>
	private static DeviceDataWiper Create(DeviceTestDatabase db)
	{
		return new DeviceDataWiper(db.UnitOfWork, db.Initializer, db.Places, db.Vehicles, db.Cards, db.Sessions);
	}

	/// <summary>
	/// Counts the rows of a table.
	/// </summary>
	/// <param name="db">The database of the test.</param>
	/// <param name="table">The table.</param>
	/// <returns>A task with the number of rows.</returns>
	private static Task<int> CountAsync(DeviceTestDatabase db, string table) => db.ScalarAsync<int>($"SELECT COUNT(*) FROM {table}");
	#endregion

	#region Wipe Tests
	/// <summary>
	/// Verifies that the profile wipe removes the places, the vehicle, the card and the sync state, and keeps the parking session and the owner.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task WipeProfileAsync_ShouldRemoveTheProfileAndKeepTheSessionAndTheOwner()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await FillAsync(db);

		var result = await Create(db).WipeProfileAsync();

		result.IsSuccess.Should().BeTrue();
		foreach (var table in new[] { "saved_places", "vehicles", "resident_cards", "sync_state" })
		{
			(await CountAsync(db, table)).Should().Be(0, table);
		}

		(await CountAsync(db, "parking_sessions")).Should().Be(1);
		(await CountAsync(db, "device_owner")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that the full wipe removes every table, including the session and the owner.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task WipeAllAsync_ShouldRemoveEverything()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await FillAsync(db);

		var result = await Create(db).WipeAllAsync();

		result.IsSuccess.Should().BeTrue();
		foreach (var table in new[] { "saved_places", "vehicles", "resident_cards", "sync_state", "parking_sessions", "device_owner" })
		{
			(await CountAsync(db, table)).Should().Be(0, table);
		}
	}

	/// <summary>
	/// Verifies that wiping an empty database succeeds, also on a file that has no schema yet.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task WipeAllAsync_EmptyDatabase_ShouldSucceed()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var result = await Create(db).WipeAllAsync();

		result.IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a commit that fails removes nothing, because the removals are one transaction.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task WipeAllAsync_LastRemovalFails_ShouldRollBackEveryRemoval()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await FillAsync(db);
		await db.ExecuteAsync("CREATE TRIGGER refuse_owner_removal BEFORE DELETE ON device_owner BEGIN SELECT RAISE(ABORT, 'refused'); END");

		var result = await Create(db).WipeAllAsync();

		result.IsFailure.Should().BeTrue();
		foreach (var table in new[] { "saved_places", "vehicles", "resident_cards", "sync_state", "parking_sessions", "device_owner" })
		{
			(await CountAsync(db, table)).Should().Be(1, table);
		}
	}
	#endregion
}
