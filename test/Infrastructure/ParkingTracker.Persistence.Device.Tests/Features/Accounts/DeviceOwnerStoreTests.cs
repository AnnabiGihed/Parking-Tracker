using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Persistence.Device.Features.Accounts;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Testing.Device;

namespace ParkingTracker.Persistence.Device.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="DeviceOwnerStore"/> against a real SQLite file: a phone with no owner answers "nothing stored", a
///              bound hash is read back with the moment of the binding, binding another hash replaces the first so that the table keeps
///              one row, and a store over a file that has no schema yet brings it up to date first.
/// </summary>
public sealed class DeviceOwnerStoreTests
{
	#region Test Infrastructure
	/// <summary>
	/// Builds the store over the database of a test.
	/// </summary>
	/// <param name="db">The database of the test.</param>
	/// <returns>The store.</returns>
	private static DeviceOwnerStore Create(DeviceTestDatabase db)
	{
		return new DeviceOwnerStore(db.Database, db.UnitOfWork, db.Initializer, db.Clock, NullLogger<DeviceOwnerStore>.Instance);
	}
	#endregion

	#region Read And Bind Tests
	/// <summary>
	/// Verifies that a phone with no owner answers "nothing stored".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAccountHashAsync_NoOwner_ShouldFailWithNotStored()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		var result = await Create(db).GetAccountHashAsync();

		DeviceDataErrors.IsNotStored(result).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a bound hash is read back, and that the moment of the binding is stored in milliseconds.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task BindAsync_ShouldStoreTheHashAndTheMomentAndReadItBack()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = Create(db);
		var hash = DeviceOwnerHash.Of(Guid.Parse("0f3c2a1b-8d4e-4f60-9a7b-123456789abc"));

		var bound = await store.BindAsync(hash);
		var read = await store.GetAccountHashAsync();

		bound.IsSuccess.Should().BeTrue();
		read.Value.Should().Be(hash);
		(await db.ScalarAsync<long>("SELECT bound_at FROM device_owner")).Should().Be(db.Clock.GetUtcNow().ToUnixTimeMilliseconds());
	}

	/// <summary>
	/// Verifies that binding another hash replaces the first one and the table keeps its one row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task BindAsync_AnotherHash_ShouldReplaceTheFirstOne()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = Create(db);
		await store.BindAsync("first");

		var bound = await store.BindAsync("second");

		bound.IsSuccess.Should().BeTrue();
		(await store.GetAccountHashAsync()).Value.Should().Be("second");
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM device_owner")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that a blank hash is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task BindAsync_BlankHash_ShouldThrow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		var bind = () => Create(db).BindAsync(" ");

		await bind.Should().ThrowAsync<ArgumentException>();
	}

	/// <summary>
	/// Verifies that the pending-removal flag is off by default and, once marked, is stored in the one sync state row, also when marked again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MarkErasePendingAsync_ShouldStoreTheFlagInTheSyncStateRowEvenTwice()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = Create(db);
		(await store.GetErasePendingAsync()).Value.Should().BeFalse();

		(await store.MarkErasePendingAsync()).IsSuccess.Should().BeTrue();
		(await store.MarkErasePendingAsync()).IsSuccess.Should().BeTrue();

		(await store.GetErasePendingAsync()).Value.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM sync_state")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that the flag survives a new store over the same file (a restart) and that the full wipe clears it with the sync state row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetErasePendingAsync_AfterRestart_ShouldStillBeSetUntilTheFullWipe()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await Create(db).MarkErasePendingAsync();
		var restarted = Create(db);
		(await restarted.GetErasePendingAsync()).Value.Should().BeTrue();
		var wiper = new DeviceDataWiper(db.UnitOfWork, db.Initializer, db.Places, db.Vehicles, db.Cards, db.Sessions);

		(await wiper.WipeAllAsync()).IsSuccess.Should().BeTrue();

		(await restarted.GetErasePendingAsync()).Value.Should().BeFalse();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM sync_state")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a file without a schema is brought up to date before the first read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAccountHashAsync_FileWithoutSchema_ShouldCreateTheSchemaFirst()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var result = await Create(db).GetAccountHashAsync();

		DeviceDataErrors.IsNotStored(result).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a database that cannot be written reports a failed result and keeps the previous owner.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task BindAsync_CommitFails_ShouldReturnFailureAndKeepTheOwner()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var store = Create(db);
		await store.BindAsync("first");
		await db.ExecuteAsync("CREATE TRIGGER refuse_owner BEFORE INSERT ON device_owner BEGIN SELECT RAISE(ABORT, 'refused'); END");

		var bound = await store.BindAsync("second");

		bound.IsFailure.Should().BeTrue();
		(await store.GetAccountHashAsync()).Value.Should().Be("first");
	}
	#endregion
}
