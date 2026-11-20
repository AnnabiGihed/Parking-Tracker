using Bunit;
using NSubstitute;
using Radzen;
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
		var cut = RenderInDialogHost<WelcomeDialog>();

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
		var cut = RenderInDialogHost<WelcomeDialog>();

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
		var cut = RenderInDialogHost<WelcomeDialog>();

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
		var cut = RenderInDialogHost<WelcomeDialog>();
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
		var cut = RenderInDialogHost<WelcomeDialog>();
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
		var cut = RenderInDialogHost<WelcomeDialog>();

		await cut.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());

		cut.FindAll(".welcome").Should().BeEmpty();
	}
	#endregion

	#region Bridge Tests
	/// <summary>
	/// Verifies that the welcome dialog opens as a Radzen bottom sheet without a title bar or close button, keeping its id for the emulator tests.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Bridge_Open_ShouldShowTheSheetWithoutTitleBarOrCloseButton()
	{
		var host = RenderInDialogHost<WelcomeDialog>();
		host.FindAll("#welcomeDialog").Should().BeEmpty();

		await host.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());

		await host.WaitForAssertionAsync(() => host.FindAll(".rz-dialog.app-sheet #welcomeDialog").Should().HaveCount(1));
		host.FindAll(".rz-dialog-titlebar, .rz-dialog-titlebar-close").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that the back button of the phone does not close the welcome dialog, which the user cannot dismiss.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Bridge_BackButton_ShouldLeaveTheDialogOpen()
	{
		var host = RenderInDialogHost<WelcomeDialog>();
		await host.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());
		await host.WaitForAssertionAsync(() => host.FindAll("#welcomeDialog").Should().HaveCount(1));

		Resolve<IBackNavigation>().CanGoBack.Should().BeFalse();
		var closed = await host.InvokeAsync(Resolve<IBackNavigation>().TryGoBack);

		closed.Should().BeFalse();
		host.FindAll("#welcomeDialog").Should().HaveCount(1);
		Resolve<WelcomeViewModel>().IsOpen.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that the welcome dialog is opened so that neither a click on the mask nor the escape key closes it, and that "Add my parking card" does.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Bridge_MaskAndEscape_ShouldBeOffAndAddCardShouldClose()
	{
		var host = RenderInDialogHost<WelcomeDialog>();
		await host.InvokeAsync(() => Resolve<WelcomeViewModel>().StartAsync());
		await host.WaitForAssertionAsync(() => host.FindAll("#welcomeDialog").Should().HaveCount(1));

		var opened = JSInterop.Invocations.Single(invocation => string.Equals(invocation.Identifier, "Radzen.openDialog", StringComparison.Ordinal));
		var options = opened.Arguments[0].Should().BeOfType<DialogOptions>().Subject;
		options.CloseDialogOnOverlayClick.Should().BeFalse();
		options.CloseDialogOnEsc.Should().BeFalse();
		options.ShowClose.Should().BeFalse();
		host.FindAll("#welcomeDialog").Should().HaveCount(1);

		await host.Find("#welcomeStart").ClickAsync();

		await host.WaitForAssertionAsync(() => host.FindAll("#welcomeDialog").Should().BeEmpty());
	}
	#endregion
}
