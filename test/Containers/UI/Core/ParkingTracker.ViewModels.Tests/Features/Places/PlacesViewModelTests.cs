using NSubstitute;
using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Domain.Features.SavedPlaces.Errors;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Places;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="PlacesViewModel"/>: a saved place moves the map and checks the spot, a place that is not set
///              opens the dialog, a place is saved from an address or from the position on screen, and every failure (address not
///              found, search failed, outside Brussels, unreadable saved data) is told in the toast.
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
		store.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		return (new PlacesViewModel(store, ui.Sender, ui.Spot, ui.Toast), store);
	}

	/// <summary>
	/// Makes the address search answer with a match.
	/// </summary>
	/// <param name="ui">The fixture that holds the sender.</param>
	/// <param name="displayName">The name the match carries.</param>
	private static void AnswerMatch(UiFixture ui, string? displayName)
	{
		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new SearchAddressResponse([new AddressMatch(Centre, displayName), new AddressMatch(Centre, "Second")])));
	}
	#endregion

	#region Go Tests
	/// <summary>
	/// Verifies that a tap on a saved place moves the map there and checks the spot, without opening the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Go_PlaceSet_ShouldShowItOnTheMap()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		var (model, store) = Arrange(ui);
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([new PlaceShortcut(PlaceKind.Home, "Home", Centre, "Grand-Place")]);
		await model.LoadAsync();
		MapViewRequest? view = null;
		ui.Spot.ViewRequested += (_, e) => view = e.Value;

		await model.GoAsync(PlaceKind.Home);

		view.Should().Be(new MapViewRequest(Centre, 18));
		ui.Spot.Pin.Should().Be(Centre);
		model.IsOpen.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a tap on a place that is not set opens the dialog for that kind.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Go_PlaceNotSet_ShouldOpenTheDialog()
	{
		using var ui = new UiFixture();
		var (model, _) = Arrange(ui);
		await model.LoadAsync();

		await model.GoAsync(PlaceKind.Work);

		model.IsOpen.Should().BeTrue();
		model.EditingKind.Should().Be(PlaceKind.Work);
		model.Editing.Should().BeNull();
		ui.Spot.Pin.Should().BeNull();
	}

	/// <summary>
	/// Verifies that closing the dialog hides it.
	/// </summary>
	[Fact]
	public void Close_Open_ShouldHideTheDialog()
	{
		using var ui = new UiFixture();
		var (model, _) = Arrange(ui);
		model.Open(PlaceKind.Home);

		model.Close();

		model.IsOpen.Should().BeFalse();
	}
	#endregion

	#region Save From Address Tests
	/// <summary>
	/// Verifies that a found address is saved with the text the service gave it, the dialog closes and the user is told.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveFromAddress_Found_ShouldSaveWithTheDisplayNameAndClose()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		AnswerMatch(ui, "Grand-Place 1, Brussels");
		model.Open(PlaceKind.Home);
		model.AddressText = " Grand-Place ";

		await model.SaveFromAddressAsync();

		await store.Received(1).SavePlaceAsync(PlaceKind.Home, Centre, "Grand-Place 1, Brussels", Arg.Any<CancellationToken>());
		model.IsOpen.Should().BeFalse();
		model.IsBusy.Should().BeFalse();
		ui.Toast.Message.Should().Be("Home saved.");
	}

	/// <summary>
	/// Verifies that the typed text is saved when the match has no name.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveFromAddress_MatchWithoutName_ShouldSaveTheTypedText()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		AnswerMatch(ui, null);
		model.Open(PlaceKind.Work);
		model.AddressText = "Rue Neuve 5";

		await model.SaveFromAddressAsync();

		await store.Received(1).SavePlaceAsync(PlaceKind.Work, Centre, "Rue Neuve 5", Arg.Any<CancellationToken>());
		ui.Toast.Message.Should().Be("Work saved.");
	}

	/// <summary>
	/// Verifies the messages for an address that was not found and for a search that failed, and that nothing is saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveFromAddress_NotFoundOrFailed_ShouldTellTheUserAndSaveNothing()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		model.Open(PlaceKind.Home);
		model.AddressText = "zzz";
		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new SearchAddressResponse([])));
		await model.SaveFromAddressAsync();
		ui.Toast.Message.Should().Be("Address not found.");

		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SearchAddressResponse>(new Error("Nominatim.Timeout", "timed out")));
		await model.SaveFromAddressAsync();

		ui.Toast.Message.Should().Be("Search failed — check your connection.");
		model.IsOpen.Should().BeTrue();
		await store.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
	}

	/// <summary>
	/// Verifies that a blank address sends no search.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveFromAddress_Blank_ShouldSendNothing()
	{
		using var ui = new UiFixture();
		var (model, _) = Arrange(ui);
		model.Open(PlaceKind.Home);
		model.AddressText = "  ";

		await model.SaveFromAddressAsync();

		await ui.Sender.DidNotReceive().Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a place outside Brussels is refused in the toast and the dialog stays open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveFromAddress_OutsideBrussels_ShouldTellTheUserAndKeepTheDialogOpen()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		store.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns(Result.Failure(SavedPlacesErrors.PointOutsideBrussels));
		AnswerMatch(ui, "Paris");
		model.Open(PlaceKind.Home);
		model.AddressText = "Paris";

		await model.SaveFromAddressAsync();

		ui.Toast.Message.Should().Be("Home has to be inside the Brussels-Capital Region.");
		model.IsOpen.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that saved data that cannot be read is told in the toast instead of being thrown.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveFromAddress_SavedDataUnreadable_ShouldTellTheUser()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		store.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns<Task<Result>>(_ => throw new SavedDataUnreadableException(SavedDataUnreadableException.PlacesMessage, "Key.Unavailable", isTemporary: true));
		AnswerMatch(ui, "Grand-Place");
		model.Open(PlaceKind.Home);
		model.AddressText = "Grand-Place";

		await model.SaveFromAddressAsync();

		ui.Toast.Message.Should().Be(SavedDataUnreadableException.PlacesMessage);
		model.IsBusy.Should().BeFalse();
	}
	#endregion

	#region Save From Position Tests
	/// <summary>
	/// Verifies that the position on screen is saved, with no address text.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveFromMyPosition_PositionKnown_ShouldSaveIt()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		var (model, store) = Arrange(ui);
		await ui.Spot.HandleMapClickAsync(Centre);
		model.Open(PlaceKind.Work);

		await model.SaveFromMyPositionAsync();

		await store.Received(1).SavePlaceAsync(PlaceKind.Work, Centre, null, Arg.Any<CancellationToken>());
		ui.Toast.Message.Should().Be("Work saved.");
	}

	/// <summary>
	/// Verifies that without a position nothing is saved and the user is told.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveFromMyPosition_NoPosition_ShouldTellTheUser()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		model.Open(PlaceKind.Home);

		await model.SaveFromMyPositionAsync();

		ui.Toast.Message.Should().Be("No position yet.");
		await store.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
	}
	#endregion

	#region Remove And Load Tests
	/// <summary>
	/// Verifies that removing clears the place, closes the dialog and tells the user.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_PlaceSet_ShouldClearItAndClose()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		IReadOnlyList<PlaceShortcut> saved = [new PlaceShortcut(PlaceKind.Work, "Work", Centre, string.Empty)];
		IReadOnlyList<PlaceShortcut> none = [];
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns(saved, none);
		await model.LoadAsync();
		model.Open(PlaceKind.Work);

		await model.RemoveAsync();

		await store.Received(1).ClearPlaceAsync(PlaceKind.Work, Arg.Any<CancellationToken>());
		model.Work.Should().BeNull();
		model.IsOpen.Should().BeFalse();
		ui.Toast.Message.Should().Be("Work removed.");
	}

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
	/// Verifies that a save the database refuses is told in the toast, the dialog stays open and the busy flag is reset.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveFromAddress_DatabaseRefused_ShouldTellTheUserAndResetBusy()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		store.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns<Task<Result>>(_ => throw new InvalidOperationException("refused"));
		AnswerMatch(ui, "Grand-Place");
		model.Open(PlaceKind.Home);
		model.AddressText = "Grand-Place";

		await model.SaveFromAddressAsync();

		ui.Toast.Message.Should().Be(PlacesViewModel.WriteFailedText);
		model.IsOpen.Should().BeTrue();
		model.IsBusy.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a remove the database refuses is told in the toast and the dialog stays open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_DatabaseRefused_ShouldTellTheUserAndKeepTheDialogOpen()
	{
		using var ui = new UiFixture();
		var (model, store) = Arrange(ui);
		store.ClearPlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("refused"));
		model.Open(PlaceKind.Home);

		await model.RemoveAsync();

		ui.Toast.Message.Should().Be(PlacesViewModel.WriteFailedText);
		model.IsOpen.Should().BeTrue();
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

		var noStore = () => new PlacesViewModel(null!, ui.Sender, ui.Spot, ui.Toast); // null on purpose: proves the guard clause rejects it

		noStore.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
