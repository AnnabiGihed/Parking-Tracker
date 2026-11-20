using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using ParkingTracker.Persistence.Device.Features.Vehicles;
using ParkingTracker.Testing.Device;

namespace ParkingTracker.Persistence.Device.Tests.Features.Vehicles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="VehicleRepository"/> against a real SQLite file: a profile round-trips with the plate encrypted at
///              rest and the enums stored as numbers, a profile without plate stores none, the database holds one profile, a stored
///              row that breaks a rule gives a failed result, and a plate ciphertext moved to another row, table or column is refused.
/// </summary>
public sealed class VehicleRepositoryTests
{
	#region Round Trip Tests
	/// <summary>
	/// Verifies that an electric car comes back with every value, that the plate is not stored as plain text and that the enums
	/// are stored as their numbers.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_ElectricVehicle_ShouldRoundTripWithEncryptedPlate()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var vehicle = TestData.ElectricVehicle();

		(await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, vehicle))).IsSuccess.Should().BeTrue();
		var loaded = await db.Vehicles.GetAsync();

		loaded.IsSuccess.Should().BeTrue();
		loaded.Value.Id.Should().Be(vehicle.Id);
		loaded.Value.ToDetails().Should().BeEquivalentTo(vehicle.ToDetails());
		loaded.Value.Plate!.Value.Should().Be("2XYZ987");
		(await db.ScalarAsync<string>("SELECT plate FROM vehicles")).Should().StartWith("k1.").And.NotContain("2XYZ987");
		(await db.ScalarAsync<int>("SELECT fuel FROM vehicles")).Should().Be(4);
		(await db.ScalarAsync<string>("SELECT connectors FROM vehicles")).Should().Be("[0,1]");
		(await db.ScalarAsync<double>("SELECT battery_kwh FROM vehicles")).Should().Be(57.5);
	}

	/// <summary>
	/// Verifies that a petrol car without a model and plate round-trips, and that the missing plate is stored as null.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_PetrolVehicleWithoutPlate_ShouldRoundTripWithNullPlate()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var vehicle = TestData.PetrolVehicle();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, vehicle));

		var loaded = await db.Vehicles.GetAsync();

		loaded.Value.ToDetails().Should().BeEquivalentTo(vehicle.ToDetails());
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles WHERE plate IS NULL AND battery_kwh IS NULL AND connectors = '[]' AND euro_norm = 1")).Should().Be(1);
	}

	/// <summary>
	/// Verifies that an empty database gives "nothing stored", and that nothing is stored before the commit.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NothingCommitted_ShouldFailWithNotStored()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var scope = await db.UnitOfWork.BeginAsync();
		await using (scope.ConfigureAwait(false))
		{
			await db.Vehicles.AddAsync(scope, TestData.ElectricVehicle());
		}

		var loaded = await db.Vehicles.GetAsync();

		loaded.Error.Should().Be(DeviceDataErrors.NotStored);
	}

	/// <summary>
	/// Verifies that an add does nothing when a profile is stored already, and that the database refuses a second row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_ProfileStoredAlready_ShouldKeepTheStoredOneAndTheDatabaseHoldsOnlyOne()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.PetrolVehicle()));

		(await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.ElectricVehicle()))).IsSuccess.Should().BeTrue();
		var second = () => db.ExecuteAsync("INSERT INTO vehicles (id, make, model_year, fuel) VALUES ('33333333-3333-3333-3333-333333333333', 'bmw', 2020, 0)");

		(await db.Vehicles.GetAsync()).Value.Fuel.Should().Be(FuelType.Petrol);
		await second.Should().ThrowAsync<SQLite.SQLiteException>();
	}

	/// <summary>
	/// Verifies that an update replaces the stored profile, and that a removal deletes it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task UpdateRemove_StoredProfile_ShouldReplaceThenDeleteIt()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.PetrolVehicle()));

		(await db.ApplyAsync(scope => db.Vehicles.UpdateAsync(scope, TestData.ElectricVehicle()))).IsSuccess.Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles")).Should().Be(1);
		(await db.Vehicles.GetAsync()).Value.Fuel.Should().Be(FuelType.Electric);
		(await db.ApplyRemovalAsync(db.Vehicles.Remove)).IsSuccess.Should().BeTrue();

		(await db.Vehicles.GetAsync()).Error.Should().Be(DeviceDataErrors.NotStored);
	}
	#endregion

	#region Invalid Row Tests
	/// <summary>
	/// Verifies that a stored row that breaks a rule of the profile gives the domain error, not an invalid aggregate.
	/// </summary>
	/// <param name="sql">The SQL that damages the row.</param>
	/// <param name="code">The expected error code.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("UPDATE vehicles SET make = 'atlantis'", "Vehicle.Make.Unknown")]
	[InlineData("UPDATE vehicles SET model_year = 1949", "Vehicle.Year.OutOfRange")]
	[InlineData("UPDATE vehicles SET fuel = 0", "Vehicle.Charging.NotAllowed")]
	[InlineData("UPDATE vehicles SET connectors = '[7]'", "Vehicle.Charging.ConnectorUnknown")]
	[InlineData("UPDATE vehicles SET euro_norm = 9", "Vehicle.EuroNorm.Unknown")]
	public async Task Get_RowBreaksARule_ShouldFailWithTheDomainError(string sql, string code)
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.ElectricVehicle()));
		await db.ExecuteAsync(sql);

		var loaded = await db.Vehicles.GetAsync();

		loaded.Error.Code.Should().Be(code);
	}

	/// <summary>
	/// Verifies that a stored model year later than the bound of today's clock still reads, so that a phone whose clock went back
	/// does not lock the user out of a profile that was valid when it was saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_StoredYearAfterTodaysBound_ShouldStillRead()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.PetrolVehicle()));
		await db.ExecuteAsync("UPDATE vehicles SET model_year = 2099");

		var loaded = await db.Vehicles.GetAsync();

		loaded.IsSuccess.Should().BeTrue();
		loaded.Value.Year.Should().Be(2099);
	}

	/// <summary>
	/// Verifies that a damaged identifier or list of plugs gives an invalid-row failure.
	/// </summary>
	/// <param name="sql">The SQL that damages the row.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("UPDATE vehicles SET id = 'not-a-guid'")]
	[InlineData("UPDATE vehicles SET connectors = 'not json'")]
	public async Task Get_DamagedColumn_ShouldFailWithInvalidRow(string sql)
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.PetrolVehicle()));
		await db.ExecuteAsync(sql);

		var loaded = await db.Vehicles.GetAsync();

		loaded.Error.Code.Should().Be(DeviceDatabaseErrors.RowInvalid("vehicles", "x").Code);
	}

	/// <summary>
	/// Verifies that a database without the table gives a failed read, not an exception.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NoTable_ShouldFailWithReadFailed()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var loaded = await db.Vehicles.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.ReadFailed);
	}
	#endregion

	#region Encryption Tests
	/// <summary>
	/// Verifies that the encrypted plate copied to another row (another identifier) is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_PlateOfAnotherRow_ShouldFailAuthentication()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.ElectricVehicle()));
		await db.ExecuteAsync("UPDATE vehicles SET id = '44444444-4444-4444-4444-444444444444'");

		var loaded = await db.Vehicles.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}

	/// <summary>
	/// Verifies that a plate encrypted for another table or another column, under the identifier of the profile, is refused: the
	/// associated data binds the value to the plate column of the vehicles table.
	/// </summary>
	/// <param name="table">The table the value was encrypted for.</param>
	/// <param name="column">The column the value was encrypted for.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("resident_cards", "plate")]
	[InlineData("vehicles", "model")]
	public async Task Get_PlateOfAnotherTableOrColumn_ShouldFailAuthentication(string table, string column)
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var vehicle = TestData.ElectricVehicle();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, vehicle));
		var foreign = await db.SealAsync(table, column, vehicle.Id.Value.ToString("D"), "2XYZ987");
		await db.ExecuteAsync($"UPDATE vehicles SET plate = '{foreign}'");

		var loaded = await db.Vehicles.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}

	/// <summary>
	/// Verifies that a plate that the key of another phone encrypted fails the read, and the profile is never returned without it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_KeyOfAnotherPhone_ShouldFailTheRead()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.ElectricVehicle()));
		var otherPhone = new VehicleRepository(db.Database, new FieldCipher(new InMemoryKeyStore(), db.Probe, NullLogger<FieldCipher>.Instance), NullLogger<VehicleRepository>.Instance);

		var loaded = await otherPhone.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}

	/// <summary>
	/// Verifies that a save with a key that cannot be read now fails and stores nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_KeyUnavailable_ShouldFailAndStoreNothing()
	{
		var keys = new InMemoryKeyStore { IsUnavailable = true };
		await using var db = await DeviceTestDatabase.CreateAsync(keys);

		var added = await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, TestData.ElectricVehicle()));

		DeviceDataErrors.IsKeyUnavailable(added).Should().BeTrue();
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles")).Should().Be(0);
	}
	#endregion

	#region Clear Plate Tests
	/// <summary>
	/// Verifies that clearing the plate sets only the plate to null and keeps every other column, so that the profile reads back without a plate.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ClearPlate_VehicleWithPlate_ShouldClearOnlyThePlate()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var vehicle = TestData.ElectricVehicle();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, vehicle));

		(await db.ApplyRemovalAsync(db.Vehicles.ClearPlate)).IsSuccess.Should().BeTrue();
		var loaded = await db.Vehicles.GetAsync();

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles WHERE plate IS NULL")).Should().Be(1);
		loaded.IsSuccess.Should().BeTrue();
		loaded.Value.Id.Should().Be(vehicle.Id);
		loaded.Value.Plate.Should().BeNull();
		loaded.Value.ToDetails().Should().BeEquivalentTo(vehicle.ToDetails() with { Plate = null });
	}

	/// <summary>
	/// Verifies that clearing the plate of a profile without one, or when none is stored, changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ClearPlate_NoPlateOrNoProfile_ShouldChangeNothing()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		(await db.ApplyRemovalAsync(db.Vehicles.ClearPlate)).IsSuccess.Should().BeTrue();
		var vehicle = TestData.PetrolVehicle();
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, vehicle));

		(await db.ApplyRemovalAsync(db.Vehicles.ClearPlate)).IsSuccess.Should().BeTrue();

		(await db.Vehicles.GetAsync()).Value.ToDetails().Should().BeEquivalentTo(vehicle.ToDetails());
	}

	/// <summary>
	/// Verifies that a missing scope is rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ClearPlate_NullScope_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var noScope = () => db.Vehicles.ClearPlate(null!); // null on purpose: proves the guard clause rejects it

		noScope.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Argument Tests
	/// <summary>
	/// Verifies that the constructor and the methods reject missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Members_NullArguments_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var noCipher = () => new VehicleRepository(db.Database, null!, NullLogger<VehicleRepository>.Instance); // null on purpose: proves the guard clause rejects it

		var scope = await db.UnitOfWork.BeginAsync();
		await using (scope.ConfigureAwait(false))
		{
			var noVehicle = () => db.Vehicles.AddAsync(scope, null!); // null on purpose: proves the guard clause rejects it
			var noUpdate = () => db.Vehicles.UpdateAsync(scope, null!); // null on purpose: proves the guard clause rejects it
			var noScope = () => db.Vehicles.AddAsync(null!, TestData.PetrolVehicle()); // null on purpose: proves the guard clause rejects it

			noCipher.Should().Throw<ArgumentNullException>();
			await noVehicle.Should().ThrowAsync<ArgumentNullException>();
			await noUpdate.Should().ThrowAsync<ArgumentNullException>();
			await noScope.Should().ThrowAsync<ArgumentNullException>();
		}
	}
	#endregion
}
