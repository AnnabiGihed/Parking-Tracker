using NSubstitute;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.Application.Tests.Features.ParkingDetection.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ParkingAlertCoordinator"/>: the background service runs only while the alerts are on and the
///              location is allowed all the time, and is stopped in every other case.
/// </summary>
public sealed class ParkingAlertCoordinatorTests
{
	#region Test Infrastructure
	/// <summary>
	/// Builds the coordinator over substitutes that answer the given switch and permissions.
	/// </summary>
	/// <param name="alerts">Whether the alerts are on.</param>
	/// <param name="foreground">The foreground permission.</param>
	/// <param name="background">The background permission.</param>
	/// <returns>The coordinator and the service substitute.</returns>
	private static (ParkingAlertCoordinator Coordinator, IParkingWatchService Service) Build(bool alerts, PermissionState foreground, PermissionState background)
	{
		var settings = Substitute.For<ISettingsStore>();
		settings.GetAlertsEnabledAsync().Returns(alerts);
		var permissions = Substitute.For<ILocationPermissions>();
		permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(foreground, background));
		var service = Substitute.For<IParkingWatchService>();
		return (new ParkingAlertCoordinator(settings, permissions, service), service);
	}
	#endregion

	#region Apply Tests
	/// <summary>
	/// Verifies that the service starts when alerts are on and the location is allowed all the time.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Apply_AlertsOnAndAllowed_ShouldStartTheService()
	{
		var (coordinator, service) = Build(true, PermissionState.Granted, PermissionState.Granted);

		var running = await coordinator.ApplyAsync();

		running.Should().BeTrue();
		await service.Received(1).StartAsync(Arg.Any<CancellationToken>());
		await service.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the service stops when the alerts are switched off, whatever the permissions are.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Apply_AlertsOff_ShouldStopTheService()
	{
		var (coordinator, service) = Build(false, PermissionState.Granted, PermissionState.Granted);

		var running = await coordinator.ApplyAsync();

		running.Should().BeFalse();
		await service.Received(1).StopAsync(Arg.Any<CancellationToken>());
		await service.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the service does not run without the permission to read the location in the background, or at all.
	/// </summary>
	/// <param name="foreground">The foreground permission.</param>
	/// <param name="background">The background permission.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(PermissionState.Granted, PermissionState.Denied)]
	[InlineData(PermissionState.Granted, PermissionState.Unknown)]
	[InlineData(PermissionState.Denied, PermissionState.Granted)]
	public async Task Apply_LocationNotAllowedAllTheTime_ShouldStopTheService(PermissionState foreground, PermissionState background)
	{
		var (coordinator, service) = Build(true, foreground, background);

		var running = await coordinator.ApplyAsync();

		running.Should().BeFalse();
		await service.Received(1).StopAsync(Arg.Any<CancellationToken>());
	}
	#endregion

	#region Guard Tests
	/// <summary>
	/// Verifies that a missing dependency is rejected.
	/// </summary>
	[Fact]
	public void Constructor_NullArguments_ShouldThrowArgumentNullException()
	{
		var settings = Substitute.For<ISettingsStore>();
		var permissions = Substitute.For<ILocationPermissions>();
		var service = Substitute.For<IParkingWatchService>();

		var noSettings = () => new ParkingAlertCoordinator(null!, permissions, service); // null on purpose: proves the guard clause rejects it
		var noPermissions = () => new ParkingAlertCoordinator(settings, null!, service); // null on purpose: proves the guard clause rejects it
		var noService = () => new ParkingAlertCoordinator(settings, permissions, null!); // null on purpose: proves the guard clause rejects it

		noSettings.Should().Throw<ArgumentNullException>();
		noPermissions.Should().Throw<ArgumentNullException>();
		noService.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
