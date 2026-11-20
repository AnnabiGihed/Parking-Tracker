using NSubstitute;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Places;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="PlacesViewModel"/>: a saved place moves the map and checks the spot, a place that is not set
///              does nothing, and a failed read (unreadable saved data, refused database) is told in the toast.
/// </summary>
public sealed class PlacesViewModelTests
{
	#region Test Infrastructure
	/// <summary>
	/// A point in the centre of Brussels.
	/// </summary>
	private static readonly GeoPoint Centre = new(50.8503, 4.3517);

	/// <summary>
	/// Builds the view model over a store substitute that holds no place.
	/// </summary>
	/// <param name="ui">The fixture that holds the screen.</param>
	/// <returns>The view model and its store.</returns>
	private static (PlacesViewModel Model, IPlacesStore Store) Arrange(UiFixture ui)
	{
		var store = Substitute.For<IPlacesStore>();
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([]);
		return (new PlacesViewModel(store, ui.Spot, ui.Toast), store);
	}
	#endregion

	#region Go Tests
	/// <summary>
	/// Verifies that showing a saved place moves the map there and checks the spot.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Show_PlaceSet_ShouldShowItOnTheMap()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		var (model, store) = Arrange(ui);
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([new PlaceShortcut(SavedPlaceId.New(), PlaceKind.Home, "Home", Centre, "Grand-Place")]);
		await model.LoadAsync();
		MapViewRequest? view = null;
		ui.Spot.ViewRequested += (_, e) => view = e.Value;

		await model.ShowAsync(PlaceKind.Home);

		view.Should().Be(new MapViewRequest(Centre, 18));
		ui.Spot.Pin.Should().Be(Centre);
	}

	/// <summary>
	/// Verifies that showing a place that is not set does nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Show_PlaceNotSet_ShouldDoNothing()
	{
		using var ui = new UiFixture();
		var (model, _) = Arrange(ui);
		await model.LoadAsync();

		await model.ShowAsync(PlaceKind.Work);

		model.PlaceOf(PlaceKind.Work).Should().BeNull();
		ui.Spot.Pin.Should().BeNull();
	}
	#endregion

	#region Load Tests
	/// <summary>
	/// Verifies that places that cannot be read leave both chips unset and are told in the toast.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Load_SavedDataUnreadable_ShouldTellTheUserAndShowNoPlace()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		store.GetPlacesAsync(Arg.Any<CancellationToken>())
			.Returns<Task<IReadOnlyList<PlaceShortcut>>>(_ => throw new SavedDataUnreadableException(SavedDataUnreadableException.PlacesMessage, "Key.Missing", isTemporary: false));

		await model.LoadAsync();

		model.Home.Should().BeNull();
		model.Work.Should().BeNull();
		ui.Toast.Message.Should().Be(SavedDataUnreadableException.PlacesMessage);
	}

	/// <summary>
	/// Verifies that a read that fails leaves both chips unset and is told in the toast.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Load_ReadFails_ShouldTellTheUserAndShowNoPlace()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<PlaceShortcut>>>(_ => throw new InvalidOperationException("refused"));

		await model.LoadAsync();

		model.Home.Should().BeNull();
		model.Work.Should().BeNull();
		ui.Toast.Message.Should().Be(PlacesViewModel.ReadFailedText);
	}

	/// <summary>
	/// Verifies that the constructor rejects missing arguments.
	/// </summary>
	[Fact]
	public void Constructor_NullArguments_ShouldThrow()
	{
		using var ui = new UiFixture();

		var noStore = () => new PlacesViewModel(null!, ui.Spot, ui.Toast); // null on purpose: proves the guard clause rejects it

		noStore.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
