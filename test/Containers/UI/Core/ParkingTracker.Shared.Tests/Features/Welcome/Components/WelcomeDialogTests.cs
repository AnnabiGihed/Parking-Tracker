using Bunit;
using NSubstitute;
using Radzen.Blazor;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Shared.Features.Welcome.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.Welcome;

namespace ParkingTracker.Shared.Tests.Features.Welcome.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the welcome dialog: the dialog explains the location permission, the
///              "Allow location all the time" button asks for the foreground permission first and then opens the settings of the
///              app, and "Add my parking card" closes the dialog for good.
/// </summary>
public sealed class WelcomeDialogTests : UiTestContext
{
	#region Dialog Tests
	/// <summary>
	/// Verifies that the dialog explains the permission and offers the button on a first launch.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_FirstLaunch_ShouldExplainThePermission()
	{
		var cut = Render<WelcomeDialog>();

		await cut.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());

		cut.Markup.Should().Contain("Location: choose “Allow all the time”");
		cut.Find("#welcomeAlways").TextContent.Should().Be("Allow location all the time");
		cut.Find("#welcomeStart").TextContent.Should().Be("Add my parking card");
	}

	/// <summary>
	/// Verifies that the dialog is built from Radzen components and keeps the selectors that the emulator test reads.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_FirstLaunch_ShouldUseRadzenComponentsAndKeepTheEmulatorSelectors()
	{
		var cut = Render<WelcomeDialog>();

		await cut.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());

		cut.Find("#welcomeDialog.welcome").Should().NotBeNull();
		cut.FindComponents<RadzenButton>().Should().HaveCount(2);
		cut.Find("#welcomeStart").ClassList.Should().Contain("rz-button");
		cut.Find("h1").TextContent.Should().Be("Park free with your residents' card");
		cut.FindAll(".benefit-icon").Should().HaveCount(4);
	}

	/// <summary>
	/// Verifies that the "Allow location all the time" button is not offered once the permission is given.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_AlwaysAllowed_ShouldNotOfferTheAllowAlwaysButton()
	{
		Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Granted, PermissionState.Granted));
		var cut = Render<WelcomeDialog>();

		await cut.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());

		cut.FindAll("#welcomeAlways").Should().BeEmpty();
		cut.Find("#welcomeStart").Should().NotBeNull();
	}

	/// <summary>
	/// Verifies the flow: the first tap asks for the foreground permission, and once it is given the next tap opens the settings.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Click_AllowAllTheTime_ShouldRequestForegroundThenOpenTheSettings()
	{
		Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Denied, PermissionState.Denied));
		Permissions.RequestForegroundAsync(Arg.Any<CancellationToken>()).Returns(true);
		var cut = Render<WelcomeDialog>();
		await cut.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());

		await cut.Find("#welcomeAlways").ClickAsync();
		await Permissions.Received(1).RequestForegroundAsync(Arg.Any<CancellationToken>());
		await Permissions.DidNotReceive().OpenSettingsAsync(Arg.Any<CancellationToken>());

		Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Granted, PermissionState.Denied));
		await cut.Find("#welcomeAlways").ClickAsync();

		await Permissions.Received(1).OpenSettingsAsync(Arg.Any<CancellationToken>());
		await cut.InvokeAsync(() => Resolve<LocationPermissionViewModel>().RefreshAsync());
		cut.Find("#welcomePermState").TextContent.Should().Contain("Allowed while using the app");
	}

	/// <summary>
	/// Verifies that "Add my parking card" closes the dialog, remembers it, and asks the page to open the card dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Click_AddMyParkingCard_ShouldCloseTheDialogForGoodAndRequestTheCardDialog()
	{
		var cut = Render<WelcomeDialog>();
		await cut.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());
		var cardRequests = 0;
		Resolve<WelcomeViewModel>().AddCardRequested += (_, _) => cardRequests++;

		await cut.Find("#welcomeStart").ClickAsync();

		_ = Settings.Received(1).SaveWelcomeSeenAsync();
		cut.FindAll("#welcomeStart").Should().BeEmpty();
		cardRequests.Should().Be(1);
	}

	/// <summary>
	/// Verifies that the dialog stays closed when the user has seen it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_AlreadySeen_ShouldStayClosed()
	{
		Settings.GetWelcomeSeenAsync().Returns(true);
		var cut = Render<WelcomeDialog>();

		await cut.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());

		cut.FindAll(".welcome").Should().BeEmpty();
	}
	#endregion
}
