using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Shared.Features.SpotCheck.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Tests.Features.SpotCheck.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for <see cref="PermissionBanner"/>: it stays hidden until the permission state is read, offers
///              "Turn on" when location is off, and offers "Allow" and "Later" when location is allowed only while the app is open.
/// </summary>
public sealed class PermissionBannerTests : UiTestContext
{
	#region Banner Tests
	/// <summary>
	/// Verifies that the banner offers "Turn on" when location is off and that the button asks for the permission.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_LocationOff_ShouldOfferTurnOn()
	{
		Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Denied, PermissionState.Denied));
		var cut = Render<PermissionBanner>();
		cut.Find("#permBanner").ClassList.Should().Contain("hidden");

		await cut.InvokeAsync(() => Resolve<LocationPermissionViewModel>().RefreshAsync());
		await cut.Find("#permBanner .btn-primary").ClickAsync();

		cut.Find("#permBanner").TextContent.Should().Contain("Location is off.");
		await Permissions.Received(1).RequestForegroundAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the banner offers "Allow" and "Later" when location is allowed only while using the app, and that "Later" hides it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_AllowedWhileUsing_ShouldOfferAllowAndLater()
	{
		Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Granted, PermissionState.Denied));
		Settings.When(settings => settings.SavePermissionSnoozeUntilAsync(Arg.Any<DateTimeOffset>())).Do(call => Settings.GetPermissionSnoozeUntilAsync().Returns(call.Arg<DateTimeOffset>()));
		var cut = Render<PermissionBanner>();
		await cut.InvokeAsync(() => Resolve<LocationPermissionViewModel>().RefreshAsync());

		cut.FindAll("#permBanner .btn").Select(button => button.TextContent).Should().Equal("Allow", "Later");
		await cut.Find("#permBanner .btn-ghost").ClickAsync();

		await cut.WaitForAssertionAsync(() => cut.Find("#permBanner").ClassList.Should().Contain("hidden"));
	}
	#endregion
}
