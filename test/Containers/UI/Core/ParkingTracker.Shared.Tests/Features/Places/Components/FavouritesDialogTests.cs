using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Shared.Features.Places.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;
using Radzen.Blazor;
using SavedPlacesList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Shared.Tests.Features.Places.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the favourite places dialog: it opens as a Radzen dialog through the bridge, lists each favourite
///              with its name and address, shows a text when there is none, disables "Add a favourite" with a reason at 20, shows
///              a favourite on the map on a tap, opens the picker from the edit button and from "Add a favourite", and closes with the
///              back button of the phone.
/// </summary>
public sealed class FavouritesDialogTests : UiTestContext
{
	#region Test Infrastructure
	/// <summary>
	/// Makes the store hold the given number of favourites, read by the view model.
	/// </summary>
	/// <param name="count">How many favourites the store holds.</param>
	/// <returns>A task that completes when the places were read.</returns>
	private async Task SaveAsync(int count)
	{
		var saved = Enumerable.Range(1, count)
			.Select(index => new PlaceShortcut(SavedPlaceId.New(), PlaceKind.Favourite, "Place " + index, Here, index == 1 ? string.Empty : "Rue " + index))
			.ToList();
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns(saved);
		await Resolve<PlacesViewModel>().LoadAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Renders the Radzen host and the dialog, and opens the list.
	/// </summary>
	/// <returns>The Radzen dialog host, which holds the list while it is open.</returns>
	private async Task<IRenderedComponent<RadzenDialog>> OpenAsync()
	{
		var host = Render<RadzenDialog>();
		Render<FavouritesDialog>();

		await host.InvokeAsync(Resolve<FavouritesViewModel>().Open).ConfigureAwait(false);

		await host.WaitForAssertionAsync(() => host.FindAll("#favouritesDialog").Should().HaveCount(1)).ConfigureAwait(false);
		return host;
	}
	#endregion

	#region List Tests
	/// <summary>
	/// Verifies that nothing is drawn while the list is closed, and that it opens in a Radzen dialog with its title.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_ShouldShowTheDialogWithItsTitle()
	{
		var closed = Render<RadzenDialog>();
		Render<FavouritesDialog>();
		closed.FindAll("#favouritesDialog").Should().BeEmpty();

		await closed.InvokeAsync(Resolve<FavouritesViewModel>().Open);

		await closed.WaitForAssertionAsync(() => closed.Find(".rz-dialog-title").TextContent.Should().Be("Favourite places"));
		Resolve<IBackNavigation>().CanGoBack.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that each favourite is a row with its name and address, or where it came from.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Rows_FavouritesSaved_ShouldShowNameAndAddress()
	{
		await SaveAsync(2);

		var host = await OpenAsync();

		host.FindAll("#favouritesList .favourite-row").Should().HaveCount(2);
		host.FindAll(".favourite-label").Select(label => label.TextContent).Should().Equal("Place 1", "Place 2");
		host.FindAll(".favourite-address").Select(address => address.TextContent).Should().Equal("Saved from the map", "Rue 2");
		host.FindAll("#favouritesEmpty").Should().BeEmpty();
		host.Find("#favouriteAdd").HasAttribute("disabled").Should().BeFalse();
		host.FindAll("#favouriteAddReason").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that the empty list says so and still offers "Add a favourite".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Rows_NoFavourites_ShouldShowTheEmptyText()
	{
		var host = await OpenAsync();

		host.Find("#favouritesEmpty").TextContent.Should().Contain("No favourite places yet");
		host.FindAll("#favouritesList").Should().BeEmpty();
		host.Find("#favouriteAdd").TextContent.Should().Be("Add a favourite");
	}

	/// <summary>
	/// Verifies that at 20 favourites "Add a favourite" is disabled and the reason is shown.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_TwentyFavourites_ShouldBeDisabledWithTheReason()
	{
		await SaveAsync(SavedPlacesList.MaximumFavourites);

		var host = await OpenAsync();

		host.Find("#favouriteAdd").HasAttribute("disabled").Should().BeTrue();
		host.Find("#favouriteAddReason").TextContent.Should().Be(FavouritesViewModel.LimitReachedText);
	}
	#endregion

	#region Tap Tests
	/// <summary>
	/// Verifies that a tap on a row closes the dialog and shows the place on the map.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Row_Tap_ShouldCloseAndShowThePlace()
	{
		await SaveAsync(1);
		AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		MapViewRequest? view = null;
		Resolve<SpotCheckViewModel>().ViewRequested += (_, e) => view = e.Value;
		var host = await OpenAsync();

		await host.Find(".favourite-main").ClickAsync();

		view.Should().NotBeNull();
		view.Center.Should().Be(Here);
		await host.WaitForAssertionAsync(() => host.FindAll("#favouritesDialog").Should().BeEmpty());
		Resolve<FavouritesViewModel>().IsOpen.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the edit button closes the dialog and opens the picker on the favourite.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Edit_Tap_ShouldOpenThePickerOnTheFavourite()
	{
		await SaveAsync(1);
		var host = await OpenAsync();

		await host.Find(".favourite-edit").ClickAsync();

		var picker = Resolve<PlacePickerViewModel>();
		picker.IsActive.Should().BeTrue();
		picker.FavouriteName.Should().Be("Place 1");
		picker.Candidate.Should().Be(Here);
		await host.WaitForAssertionAsync(() => host.FindAll("#favouritesDialog").Should().BeEmpty());
	}

	/// <summary>
	/// Verifies that "Add a favourite" closes the dialog and starts the picker in favourite mode.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_Tap_ShouldStartThePickerForANewFavourite()
	{
		var host = await OpenAsync();

		await host.Find("#favouriteAdd").ClickAsync();

		var picker = Resolve<PlacePickerViewModel>();
		picker.IsActive.Should().BeTrue();
		picker.Kind.Should().Be(PlaceKind.Favourite);
		picker.HasSavedPlace.Should().BeFalse();
		await host.WaitForAssertionAsync(() => host.FindAll("#favouritesDialog").Should().BeEmpty());
	}

	/// <summary>
	/// Verifies that the back button of the phone closes the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task BackButton_ShouldCloseTheDialog()
	{
		var host = await OpenAsync();

		var closed = await host.InvokeAsync(Resolve<IBackNavigation>().TryGoBack);

		closed.Should().BeTrue();
		await host.WaitForAssertionAsync(() => host.FindAll("#favouritesDialog").Should().BeEmpty());
		Resolve<FavouritesViewModel>().IsOpen.Should().BeFalse();
	}
	#endregion
}
