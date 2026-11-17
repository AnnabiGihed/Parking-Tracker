using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="LocationPermissionViewModel"/>: which banner shows, the foreground request followed by the
///              settings of the app for "Allow all the time", and the postponement of the banner for a week.
/// </summary>
public sealed class LocationPermissionViewModelTests
{
	#region Test Infrastructure
	/// <summary>
	/// Makes the permissions substitute answer with a status.
	/// </summary>
	/// <param name="ui">The fixture.</param>
	/// <param name="foreground">The foreground permission.</param>
	/// <param name="background">The background permission.</param>
	private static void Allow(UiFixture ui, PermissionState foreground, PermissionState background)
	{
		ui.Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(foreground, background));
	}
	#endregion

	#region Banner Tests
	/// <summary>
	/// Verifies which banner each combination of permissions gives.
	/// </summary>
	/// <param name="foreground">The foreground permission.</param>
	/// <param name="background">The background permission.</param>
	/// <param name="expected">The expected banner.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(PermissionState.Denied, PermissionState.Denied, PermissionBannerKind.TurnOn)]
	[InlineData(PermissionState.Granted, PermissionState.Denied, PermissionBannerKind.AllowAlways)]
	[InlineData(PermissionState.Granted, PermissionState.Granted, PermissionBannerKind.None)]
	[InlineData(PermissionState.Unknown, PermissionState.Unknown, PermissionBannerKind.None)]
	public async Task Refresh_Permissions_ShouldPickTheBanner(PermissionState foreground, PermissionState background, PermissionBannerKind expected)
	{
		using var ui = new UiFixture();
		Allow(ui, foreground, background);

		await ui.Permission.RefreshAsync();

		ui.Permission.Banner.Should().Be(expected);
	}

	/// <summary>
	/// Verifies the sentence of the welcome dialog and the button that offers "all the time".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_Allowed_ShouldWriteTheWelcomeSentence()
	{
		using var ui = new UiFixture();
		Allow(ui, PermissionState.Granted, PermissionState.Denied);
		await ui.Permission.RefreshAsync();
		ui.Permission.WelcomeMessage.Should().Be("✓ Allowed while using the app. Tap the button to allow it all the time.");
		ui.Permission.ShowAllowAlwaysButton.Should().BeTrue();

		Allow(ui, PermissionState.Granted, PermissionState.Granted);
		await ui.Permission.RefreshAsync();

		ui.Permission.WelcomeMessage.Should().Be("✓ Location allowed all the time.");
		ui.Permission.ShowAllowAlwaysButton.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that "Later" hides the "all the time" banner for a week and then lets it come back.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Postpone_Later_ShouldHideTheBannerForAWeek()
	{
		using var ui = new UiFixture();
		Allow(ui, PermissionState.Granted, PermissionState.Denied);
		ui.Settings.When(settings => settings.SavePermissionSnoozeUntilAsync(Arg.Any<DateTimeOffset>())).Do(call => ui.Settings.GetPermissionSnoozeUntilAsync().Returns(call.Arg<DateTimeOffset>()));

		await ui.Permission.PostponeAsync();

		_ = ui.Settings.Received(1).SavePermissionSnoozeUntilAsync(UiFixture.Start.AddDays(7));
		ui.Permission.Banner.Should().Be(PermissionBannerKind.None);
		ui.Clock.Advance(TimeSpan.FromDays(7));
		await ui.Permission.RefreshAsync();
		ui.Permission.Banner.Should().Be(PermissionBannerKind.AllowAlways);
	}
	#endregion

	#region Flow Tests
	/// <summary>
	/// Verifies that "Allow location all the time" first asks for the foreground permission, explains the next step and lets the
	/// tracking start when it is given.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AllowAlways_ForegroundNotGranted_ShouldRequestItAndExplainTheNextStep()
	{
		using var ui = new UiFixture();
		Allow(ui, PermissionState.Denied, PermissionState.Denied);
		ui.Permissions.RequestForegroundAsync(Arg.Any<CancellationToken>()).Returns(true);
		var granted = false;
		ui.Permission.ForegroundGranted += (_, _) => granted = true;

		await ui.Permission.AllowAlwaysAsync();

		await ui.Permissions.Received(1).RequestForegroundAsync(Arg.Any<CancellationToken>());
		await ui.Permissions.DidNotReceive().OpenSettingsAsync(Arg.Any<CancellationToken>());
		ui.Toast.Message.Should().Be("Allow location, then choose “All the time” in the next step");
		granted.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that once the foreground permission is given, "Allow location all the time" opens the settings of the app.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AllowAlways_ForegroundGranted_ShouldOpenTheSettingsOfTheApp()
	{
		using var ui = new UiFixture();
		Allow(ui, PermissionState.Granted, PermissionState.Denied);

		await ui.Permission.AllowAlwaysAsync();

		await ui.Permissions.Received(1).OpenSettingsAsync(Arg.Any<CancellationToken>());
		await ui.Permissions.DidNotReceive().RequestForegroundAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that "Turn on" asks for the permission when it is denied and opens the settings otherwise.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task TurnOn_DeniedOrNot_ShouldRequestOrOpenTheSettings()
	{
		using var ui = new UiFixture();
		Allow(ui, PermissionState.Denied, PermissionState.Denied);
		await ui.Permission.RefreshAsync();
		await ui.Permission.TurnOnAsync();
		await ui.Permissions.Received(1).RequestForegroundAsync(Arg.Any<CancellationToken>());

		Allow(ui, PermissionState.Granted, PermissionState.Granted);
		await ui.Permission.RefreshAsync();
		await ui.Permission.TurnOnAsync();

		await ui.Permissions.Received(1).OpenSettingsAsync(Arg.Any<CancellationToken>());
	}
	#endregion
}
