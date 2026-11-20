using NSubstitute;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.ViewModels.Features.Menu;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Menu;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="MenuViewModel"/>: the text under Home, Work and the parking card, and the screens its entries open.
/// </summary>
public sealed class MenuViewModelTests
{
	#region Test Infrastructure
	/// <summary>
	/// A point in the centre of Brussels.
	/// </summary>
	private static readonly GeoPoint Centre = new(50.8503, 4.3517);

	/// <summary>
	/// Builds the menu over a places store that holds the given places.
	/// </summary>
	/// <param name="ui">The fixture that holds the card and the other view models.</param>
	/// <param name="saved">The places the store holds.</param>
	/// <returns>The menu and the places view model it reads.</returns>
	private static async Task<(MenuViewModel Menu, PlacesViewModel Places)> ArrangeAsync(UiFixture ui, params PlaceShortcut[] saved)
	{
		var store = Substitute.For<IPlacesStore>();
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns(saved);
		var places = new PlacesViewModel(store, ui.Sender, ui.Spot, ui.Toast);
		await places.LoadAsync().ConfigureAwait(false);
		return (new MenuViewModel(places, ui.Card), places);
	}
	#endregion

	#region Text Tests
	/// <summary>
	/// Verifies that places and card that are not set read "Not set".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Texts_NothingSaved_ShouldReadNotSet()
	{
		using var ui = new UiFixture();
		var (menu, _) = await ArrangeAsync(ui);

		menu.HomeText.Should().Be("Not set");
		menu.WorkText.Should().Be("Not set");
		menu.CardText.Should().Be("Not set");
	}

	/// <summary>
	/// Verifies that a saved place shows its address, and a place saved from the map shows that instead of an empty line.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Texts_PlacesSaved_ShouldShowTheAddressOrWhereItCameFrom()
	{
		using var ui = new UiFixture();

		var (menu, _) = await ArrangeAsync(ui, new PlaceShortcut(PlaceKind.Home, "Home", Centre, "Grand-Place 1"), new PlaceShortcut(PlaceKind.Work, "Work", Centre, string.Empty));

		menu.HomeText.Should().Be("Grand-Place 1");
		menu.WorkText.Should().Be("Saved from the map");
	}

	/// <summary>
	/// Verifies that a change of the places is passed on, so that an open menu renders again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Changed_PlacesChange_ShouldBeRaised()
	{
		using var ui = new UiFixture();
		var (menu, places) = await ArrangeAsync(ui);
		var raised = 0;
		menu.Changed += (_, _) => raised++;

		places.Open(PlaceKind.Home);

		raised.Should().Be(1);
	}
	#endregion

	#region Open Tests
	/// <summary>
	/// Verifies that the Work entry opens the place dialog for Work.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenPlace_Work_ShouldOpenThePlaceDialogForWork()
	{
		using var ui = new UiFixture();
		var (menu, places) = await ArrangeAsync(ui);

		menu.OpenPlace(PlaceKind.Work);

		places.IsOpen.Should().BeTrue();
		places.EditingKind.Should().Be(PlaceKind.Work);
	}

	/// <summary>
	/// Verifies that the card entry opens the card dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenCardAsync_ShouldOpenTheCardDialog()
	{
		using var ui = new UiFixture();
		var (menu, _) = await ArrangeAsync(ui);

		await menu.OpenCardAsync();

		ui.Card.IsOpen.Should().BeTrue();
	}
	#endregion

	#region Constructor Tests
	/// <summary>
	/// Verifies that a missing dependency is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Constructor_NullArguments_ShouldThrowArgumentNullException()
	{
		using var ui = new UiFixture();
		var (_, places) = await ArrangeAsync(ui);

		var noPlaces = () => new MenuViewModel(null!, ui.Card); // null on purpose: proves the guard clause rejects it
		var noCard = () => new MenuViewModel(places, null!); // null on purpose: proves the guard clause rejects it

		noPlaces.Should().Throw<ArgumentNullException>();
		noCard.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
