using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Testing.Device;

namespace ParkingTracker.Persistence.Device.Tests.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="EncryptedDataProbe"/> on the real SQLite file: an empty database and a database whose tables were
///              never created hold no encrypted value; a card with a number, a parking session, a saved place or a vehicle profile with
///              a plate does; a card without a number and plate and a vehicle profile without a plate do not; a database whose schema
///              is not yet at the latest step still counts the tables it has; a database that cannot be read is a failure, never "none".
/// </summary>
public sealed class EncryptedDataProbeTests
{
	#region Any Tests
	/// <summary>
	/// Verifies that a database without rows holds no encrypted value.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Any_EmptyDatabase_ShouldBeFalse()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		var any = await db.Probe.AnyAsync();

		any.Value.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a file whose schema was never created holds nothing that can be lost.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Any_NoTables_ShouldBeFalse()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var any = await db.Probe.AnyAsync();

		any.IsSuccess.Should().BeTrue();
		any.Value.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a card with an encrypted number and plate counts.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Any_CardWithNumberAndPlate_ShouldBeTrue()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		(await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()))).IsSuccess.Should().BeTrue();

		var any = await db.Probe.AnyAsync();

		any.Value.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a card without a number and plate holds nothing encrypted.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Any_CardWithoutNumberAndPlate_ShouldBeFalse()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		(await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.IxellesCard()))).IsSuccess.Should().BeTrue();

		var any = await db.Probe.AnyAsync();

		any.Value.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a parked car counts.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Any_Session_ShouldBeTrue()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		(await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session()))).IsSuccess.Should().BeTrue();

		var any = await db.Probe.AnyAsync();

		any.Value.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a vehicle profile with an encrypted plate counts.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Any_VehicleWithPlate_ShouldBeTrue()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		(await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.ElectricVehicle()))).IsSuccess.Should().BeTrue();

		var any = await db.Probe.AnyAsync();

		any.Value.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a vehicle profile without a plate holds nothing encrypted.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Any_VehicleWithoutPlate_ShouldBeFalse()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		(await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.PetrolVehicle()))).IsSuccess.Should().BeTrue();

		var any = await db.Probe.AnyAsync();

		any.Value.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a database still at version 1, without the vehicles table, counts the encrypted rows of the tables it has, so
	/// that a missing key is never replaced while a card is stored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Any_VersionOneDatabaseWithCard_ShouldBeTrue()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		(await db.ApplyAsync(scope => db.Cards.AddAsync(scope, TestData.CityCard()))).IsSuccess.Should().BeTrue();
		await db.ExecuteAsync("DROP TABLE vehicles");

		var any = await db.Probe.AnyAsync();

		any.Value.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a database that cannot be read is a failure and not "no rows".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Any_BrokenTables_ShouldFailAndNotAnswerNone()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ExecuteAsync("DROP TABLE resident_cards");
		await db.ExecuteAsync("CREATE TABLE resident_cards (id TEXT)");

		var any = await db.Probe.AnyAsync();

		any.IsFailure.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that the constructor rejects missing arguments.
	/// </summary>
	[Fact]
	public void Constructor_NullArguments_ShouldThrow()
	{
		var noDatabase = () => new EncryptedDataProbe(null!, NullLogger<EncryptedDataProbe>.Instance); // null on purpose: proves the guard clause rejects it
		var noLogger = () => new EncryptedDataProbe(new DeviceDatabase("x.db"), null!); // null on purpose: proves the guard clause rejects it

		noDatabase.Should().Throw<ArgumentNullException>();
		noLogger.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
