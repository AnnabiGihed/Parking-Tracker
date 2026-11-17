using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Welcome;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ParkingTracker.ViewModels.Features.Welcome.WelcomeViewModel"/>: the dialog opens only on a
///              first launch without a card, "Add my parking card" closes it for good and asks for the card form, and the
///              permission button goes through the permission flow.
/// </summary>
public sealed class WelcomeViewModelTests
{
	#region Start Tests
	/// <summary>
	/// Verifies that the dialog opens without a card on a first launch and reads the permissions.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_FirstLaunchWithoutCard_ShouldOpenAndReadThePermissions()
	{
		using var ui = new UiFixture();

		await ui.Welcome.StartAsync();

		ui.Welcome.IsOpen.Should().BeTrue();
		await ui.Permissions.Received(1).GetStatusAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the dialog stays closed when a card exists or the dialog was seen.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_CardOrSeen_ShouldStayClosed()
	{
		using var withCard = new UiFixture();
		withCard.Settings.GetCardAsync().Returns(new ParkingCard("bruxelles", "louise", new CardPeriod(null, null), [], [], []));
		using var seen = new UiFixture();
		seen.Settings.GetWelcomeSeenAsync().Returns(true);

		await withCard.Welcome.StartAsync();
		await seen.Welcome.StartAsync();

		withCard.Welcome.IsOpen.Should().BeFalse();
		seen.Welcome.IsOpen.Should().BeFalse();
	}
	#endregion

	#region Action Tests
	/// <summary>
	/// Verifies that "Add my parking card" remembers the dialog was seen, closes it and asks for the card form.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddCard_Tap_ShouldCloseForGoodAndAskForTheCardForm()
	{
		using var ui = new UiFixture();
		await ui.Welcome.StartAsync();
		var asked = false;
		ui.Welcome.AddCardRequested += (_, _) => asked = true;

		await ui.Welcome.AddCardAsync();

		ui.Welcome.IsOpen.Should().BeFalse();
		_ = ui.Settings.Received(1).SaveWelcomeSeenAsync();
		asked.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that "Allow location all the time" goes through the permission flow.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AllowLocationAlways_Tap_ShouldGoThroughThePermissionFlow()
	{
		using var ui = new UiFixture();
		ui.Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Granted, PermissionState.Denied));

		await ui.Welcome.AllowLocationAlwaysAsync();

		await ui.Permissions.Received(1).OpenSettingsAsync(Arg.Any<CancellationToken>());
	}
	#endregion
}
