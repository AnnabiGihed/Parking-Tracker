using NSubstitute;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using SavedPlacesList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.ViewModels.Tests.Features.Places;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="FavouritesViewModel"/>: it lists the favourites, opens and closes like a dialog, a tap shows the
///              place and closes it, edit and add open the map picker and close it, an empty list is flagged, and adding is refused
///              with a reason once 20 favourites exist.
/// </summary>
public sealed class FavouritesViewModelTests
{
	#region Test Infrastructure
	/// <summary>
	/// A point in the centre of Brussels.
	/// </summary>
	private static readonly GeoPoint Centre = new(50.8503, 4.3517);

	/// <summary>
	/// Builds the list over a store that holds the given number of favourites.
	/// </summary>
	/// <param name="ui">The fixture that holds the screen.</param>
	/// <param name="count">How many favourites the store holds.</param>
	/// <returns>The list, the picker it opens and the favourites it holds.</returns>
	private static async Task<(FavouritesViewModel Favourites, PlacePickerViewModel Picker, IReadOnlyList<PlaceShortcut> Saved)> ArrangeAsync(UiFixture ui, int count)
	{
		var saved = Enumerable.Range(1, count)
			.Select(index => new PlaceShortcut(SavedPlaceId.New(), PlaceKind.Favourite, "Place " + index, Centre, index == 1 ? string.Empty : "Rue " + index))
			.ToList();
		var store = Substitute.For<IPlacesStore>();
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns(saved);
		var places = new PlacesViewModel(store, ui.Spot, ui.Toast);
		await places.LoadAsync().ConfigureAwait(false);
		var picker = new PlacePickerViewModel(store, ui.Sender, ui.Spot, places, ui.Toast);
		return (new FavouritesViewModel(places, picker), picker, saved);
	}
	#endregion

	#region List Tests
	/// <summary>
	/// Verifies that an empty list is flagged and can add, and that favourites are listed with an address or the place it came from.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Favourites_EmptyAndFilled_ShouldReportThem()
	{
		using var ui = new UiFixture();
		var (empty, _, _) = await ArrangeAsync(ui, 0);
		var (filled, _, saved) = await ArrangeAsync(ui, 2);

		empty.IsEmpty.Should().BeTrue();
		empty.CanAdd.Should().BeTrue();
		empty.AddDisabledReason.Should().BeNull();
		filled.IsEmpty.Should().BeFalse();
		filled.Favourites.Should().HaveCount(2);
		FavouritesViewModel.AddressOf(saved[0]).Should().Be("Saved from the map");
		FavouritesViewModel.AddressOf(saved[1]).Should().Be("Rue 2");
	}

	/// <summary>
	/// Verifies that adding is disabled with a reason at 20 favourites and does nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_TwentyFavourites_ShouldBeDisabledWithAReason()
	{
		using var ui = new UiFixture();
		var (almost, _, _) = await ArrangeAsync(ui, SavedPlacesList.MaximumFavourites - 1);
		var (full, picker, _) = await ArrangeAsync(ui, SavedPlacesList.MaximumFavourites);
		full.Open();

		full.Add();

		almost.CanAdd.Should().BeTrue();
		full.CanAdd.Should().BeFalse();
		full.AddDisabledReason.Should().Be(FavouritesViewModel.LimitReachedText);
		full.IsOpen.Should().BeTrue();
		picker.IsActive.Should().BeFalse();
	}
	#endregion

	#region Dialog Tests
	/// <summary>
	/// Verifies that Open and Close raise the change once each and that a second call changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenAndClose_ShouldRaiseChangedOnceEach()
	{
		using var ui = new UiFixture();
		var (favourites, _, _) = await ArrangeAsync(ui, 1);
		var raised = 0;
		favourites.Changed += (_, _) => raised++;

		favourites.Open();
		favourites.Open();
		favourites.Close();
		favourites.Close();

		raised.Should().Be(2);
		favourites.IsOpen.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a tap on a favourite closes the list and shows the place on the map.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Show_Favourite_ShouldCloseAndShowItOnTheMap()
	{
		using var ui = new UiFixture();
		var (favourites, _, saved) = await ArrangeAsync(ui, 1);
		ui.AnswerChecks(UiFixture.InZone(Verdict.Pay));
		MapViewRequest? view = null;
		ui.Spot.ViewRequested += (_, e) => view = e.Value;
		favourites.Open();

		await favourites.ShowAsync(saved[0]);

		favourites.IsOpen.Should().BeFalse();
		view.Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that edit opens the picker on the favourite and Add starts a new one, each closing the list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EditAndAdd_ShouldCloseTheListAndOpenThePicker()
	{
		using var ui = new UiFixture();
		var (favourites, picker, saved) = await ArrangeAsync(ui, 1);
		favourites.Open();

		favourites.Edit(saved[0]);

		favourites.IsOpen.Should().BeFalse();
		picker.IsActive.Should().BeTrue();
		picker.FavouriteName.Should().Be("Place 1");
		picker.Cancel();
		favourites.Open();

		favourites.Add();

		favourites.IsOpen.Should().BeFalse();
		picker.State.Should().Be(PlacePickerState.Searching);
		picker.Kind.Should().Be(PlaceKind.Favourite);
		picker.HasSavedPlace.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that missing arguments are rejected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task NullArguments_ShouldThrow()
	{
		using var ui = new UiFixture();
		var (favourites, picker, _) = await ArrangeAsync(ui, 1);
		var places = new PlacesViewModel(Substitute.For<IPlacesStore>(), ui.Spot, ui.Toast);

		var noPlaces = () => new FavouritesViewModel(null!, picker); // null on purpose: proves the guard clause rejects it
		var noPicker = () => new FavouritesViewModel(places, null!); // null on purpose: proves the guard clause rejects it
		var noShow = () => favourites.ShowAsync(null!); // null on purpose: proves the guard clause rejects it
		var noEdit = () => favourites.Edit(null!); // null on purpose: proves the guard clause rejects it
		var noAddress = () => FavouritesViewModel.AddressOf(null!); // null on purpose: proves the guard clause rejects it

		noPlaces.Should().Throw<ArgumentNullException>();
		noPicker.Should().Throw<ArgumentNullException>();
		await noShow.Should().ThrowAsync<ArgumentNullException>();
		noEdit.Should().Throw<ArgumentNullException>();
		noAddress.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
