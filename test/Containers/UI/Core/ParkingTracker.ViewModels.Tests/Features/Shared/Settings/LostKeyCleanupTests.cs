using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Testing.Device;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="LostKeyCleanup"/> over a real SQLite file: after a lost key the helper removes the card, the parking session and
///              the saved places, clears only the plate of the vehicle and keeps the rest of it, commits in the scope, and raises the notice once; a
///              refused commit throws and raises nothing; missing arguments are rejected.
/// </summary>
public sealed class LostKeyCleanupTests
{
	#region Cleanup Tests
	/// <summary>
	/// Verifies that the helper removes the places, clears the plate of the vehicle, keeps its other values and raises the notice once.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RemoveStaleRows_LostKey_ShouldRemoveRowsClearThePlateAndRaiseTheNotice()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var places = new DevicePlacesStore(db.Places, db.Cards, db.Vehicles, db.Sessions, db.UnitOfWork, db.Initializer, new FakeStartupGate(), NullLogger<DevicePlacesStore>.Instance, new DeviceDataResetNotice());
		await places.SavePlaceAsync(PlaceKind.Home, new GeoPoint(50.8467, 4.3525), null);
		var vehicle = Vehicle.Create(new VehicleDetails("renault", "Clio", 2019, FuelType.Petrol, EuroNorm.Euro6, null, "1ABC234"), 2026).Value;
		await db.ApplyAsync(scope => db.Vehicles.AddAsync(scope, vehicle));
		var notice = new DeviceDataResetNotice();
		var raised = 0;
		notice.StaleRowsRemoved += (_, _) => raised++;

		var scope = await db.UnitOfWork.BeginAsync();
		await using (scope.ConfigureAwait(false))
		{
			await LostKeyCleanup.RemoveStaleRowsAsync(scope, db.Cards, db.Sessions, db.Places, db.Vehicles, notice);
		}

		raised.Should().Be(1);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM saved_places")).Should().Be(0);
		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles")).Should().Be(1);
		(await db.Vehicles.GetAsync()).Value.ToDetails().Should().BeEquivalentTo(vehicle.ToDetails() with { Plate = null });
	}

	/// <summary>
	/// Verifies that a commit that the database refuses throws and does not raise the notice.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RemoveStaleRows_CommitRefused_ShouldThrowAndNotRaise()
	{
		var scope = Substitute.For<IDeviceChangeScope>();
		scope.CommitAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("Test.Refused", "The database refused the change.")));
		var notice = new DeviceDataResetNotice();
		var raised = 0;
		notice.StaleRowsRemoved += (_, _) => raised++;

		var clean = () => LostKeyCleanup.RemoveStaleRowsAsync(scope, Substitute.For<IResidentCardRepository>(), Substitute.For<IParkingSessionRepository>(), Substitute.For<ISavedPlacesRepository>(), Substitute.For<IVehicleRepository>(), notice);

		await clean.Should().ThrowAsync<InvalidOperationException>().WithMessage("The database refused the change.");
		raised.Should().Be(0);
	}
	#endregion

	#region Argument Tests
	/// <summary>
	/// Verifies that missing arguments are rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RemoveStaleRows_NullArguments_ShouldThrow()
	{
		var scope = Substitute.For<IDeviceChangeScope>();
		var cards = Substitute.For<IResidentCardRepository>();
		var sessions = Substitute.For<IParkingSessionRepository>();
		var places = Substitute.For<ISavedPlacesRepository>();
		var vehicles = Substitute.For<IVehicleRepository>();
		var notice = new DeviceDataResetNotice();

		var noScope = () => LostKeyCleanup.RemoveStaleRowsAsync(null!, cards, sessions, places, vehicles, notice); // null on purpose: proves the guard clause rejects it
		var noCards = () => LostKeyCleanup.RemoveStaleRowsAsync(scope, null!, sessions, places, vehicles, notice); // null on purpose: proves the guard clause rejects it
		var noSessions = () => LostKeyCleanup.RemoveStaleRowsAsync(scope, cards, null!, places, vehicles, notice); // null on purpose: proves the guard clause rejects it
		var noPlaces = () => LostKeyCleanup.RemoveStaleRowsAsync(scope, cards, sessions, null!, vehicles, notice); // null on purpose: proves the guard clause rejects it
		var noVehicles = () => LostKeyCleanup.RemoveStaleRowsAsync(scope, cards, sessions, places, null!, notice); // null on purpose: proves the guard clause rejects it
		var noNotice = () => LostKeyCleanup.RemoveStaleRowsAsync(scope, cards, sessions, places, vehicles, null!); // null on purpose: proves the guard clause rejects it

		await noScope.Should().ThrowAsync<ArgumentNullException>();
		await noCards.Should().ThrowAsync<ArgumentNullException>();
		await noSessions.Should().ThrowAsync<ArgumentNullException>();
		await noPlaces.Should().ThrowAsync<ArgumentNullException>();
		await noVehicles.Should().ThrowAsync<ArgumentNullException>();
		await noNotice.Should().ThrowAsync<ArgumentNullException>();
	}
	#endregion
}
