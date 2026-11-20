using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Shared.Features.Places.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.Shared.Tests.Features.Places.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the Home and Work chips and the place dialog: the chips render, a tap on a place that is not set
///              opens the dialog, a saved place gets an edit button, and the dialog offers Remove only when the place is saved.
/// </summary>
public sealed class PlaceComponentsTests : UiTestContext
{
	#region Test Infrastructure
	/// <summary>
	/// A saved home.
	/// </summary>
	private static readonly PlaceShortcut SavedHome = new(PlaceKind.Home, "Home", Here, "Rue de la Loi 1");
	#endregion

	#region Chip Tests
	/// <summary>
	/// Verifies that both chips render without edit buttons while no place is saved, and that a tap opens the dialog of that kind.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlaceChips_NothingSaved_ShouldRenderTwoChipsAndOpenTheDialogOnTap()
	{
		var chips = Render<PlaceChips>();

		chips.Find("#placeHome").TextContent.Should().Contain("Set home");
		chips.Find("#placeWork").TextContent.Should().Contain("Set work");
		chips.FindAll("#placeHomeEdit").Should().BeEmpty();

		await chips.Find("#placeWork").ClickAsync();

		var model = Resolve<PlacesViewModel>();
		model.IsOpen.Should().BeTrue();
		model.EditingKind.Should().Be(PlaceKind.Work);
	}

	/// <summary>
	/// Verifies that the chips are Radzen chips with the ids the end-to-end tests use.
	/// </summary>
	[Fact]
	public void PlaceChips_NothingSaved_ShouldRenderRadzenChipsWithTheirIds()
	{
		var chips = Render<PlaceChips>();

		chips.Find("#placeHome").ClassList.Should().Contain("rz-chip");
		chips.Find("#placeWork").ClassList.Should().Contain("rz-chip");
	}

	/// <summary>
	/// Verifies that a saved place reads "Home" and that a tap on it shows the place instead of opening the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlaceChips_HomeSaved_ShouldShowThePlaceOnTap()
	{
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([SavedHome]);
		var chips = Render<PlaceChips>();
		await chips.WaitForAssertionAsync(() => chips.Find("#placeHome").TextContent.Should().NotContain("Set"));
		AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		MapViewRequest? view = null;
		Resolve<SpotCheckViewModel>().ViewRequested += (_, e) => view = e.Value;

		await chips.Find("#placeHome").ClickAsync();

		view.Should().NotBeNull();
		Resolve<PlacesViewModel>().IsOpen.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a saved place gets an edit button that opens the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlaceChips_HomeSaved_ShouldShowTheEditButtonThatOpensTheDialog()
	{
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([SavedHome]);
		var chips = Render<PlaceChips>();

		await chips.WaitForAssertionAsync(() => chips.Find("#placeHomeEdit").GetAttribute("aria-label").Should().Be("Edit home"));
		await chips.Find("#placeHomeEdit").ClickAsync();

		Resolve<PlacesViewModel>().IsOpen.Should().BeTrue();
		chips.FindAll("#placeWorkEdit").Should().BeEmpty();
	}
	#endregion

	#region Dialog Tests
	/// <summary>
	/// Verifies that the dialog of a place that is not set offers no Remove button and is titled with its kind.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlaceDialog_NotSet_ShouldOfferNoRemove()
	{
		var dialog = Render<PlaceDialog>();

		await dialog.InvokeAsync(() => Resolve<PlacesViewModel>().Open(PlaceKind.Work));

		dialog.Markup.Should().Contain("Work");
		dialog.Find("#placeSearch").TextContent.Should().Contain("Search");
		dialog.Find("#placeHere").TextContent.Should().Contain("Use my position");
		dialog.FindAll("#placeClear").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that the dialog of a saved place offers Remove, and that Remove clears the place.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlaceDialog_Saved_ShouldOfferRemoveThatClearsThePlace()
	{
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([SavedHome]);
		var dialog = Render<PlaceDialog>();
		await dialog.InvokeAsync(async () =>
		{
			var model = Resolve<PlacesViewModel>();
			await model.LoadAsync().ConfigureAwait(false);
			model.Open(PlaceKind.Home);
		});

		await dialog.Find("#placeClear").ClickAsync();

		await Places.Received(1).ClearPlaceAsync(PlaceKind.Home, Arg.Any<CancellationToken>());
	}
	#endregion
}
