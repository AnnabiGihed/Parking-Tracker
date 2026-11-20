using NSubstitute;
using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Domain.Features.SavedPlaces.Errors;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Places;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="PlacePickerViewModel"/>: one match goes straight to the confirm step and several to the
///              list, a pick, the position and a moved pin choose a place, Save writes exactly once and only with the confirmed
///              point, Cancel saves nothing, a place outside Brussels cannot be saved, and every refusal or failure is told in the toast.
/// </summary>
public sealed class PlacePickerViewModelTests
{
	#region Test Infrastructure
	/// <summary>
	/// A point in the centre of Brussels.
	/// </summary>
	private static readonly GeoPoint Centre = new(50.8503, 4.3517);

	/// <summary>
	/// Another point in Brussels.
	/// </summary>
	private static readonly GeoPoint Elsewhere = new(50.8250, 4.3606);

	/// <summary>
	/// A point in Paris.
	/// </summary>
	private static readonly GeoPoint Paris = new(48.8566, 2.3522);

	/// <summary>
	/// A match with a label and a locality.
	/// </summary>
	private static readonly AddressMatch GrandPlace = new(Centre, "Grand-Place 1, 1000 Brussels", "Grand-Place 1", "1000 Brussels");

	/// <summary>
	/// A second match.
	/// </summary>
	private static readonly AddressMatch RueNeuve = new(Elsewhere, "Rue Neuve 5, 1000 Brussels", "Rue Neuve 5", "1000 Brussels");

	/// <summary>
	/// Builds the picker over a store substitute that holds the given places.
	/// </summary>
	/// <param name="ui">The fixture that holds the screen.</param>
	/// <param name="saved">The places the store holds.</param>
	/// <returns>The picker, the places view model and the store.</returns>
	private static async Task<(PlacePickerViewModel Picker, PlacesViewModel Places, IPlacesStore Store)> ArrangeAsync(UiFixture ui, params PlaceShortcut[] saved)
	{
		var store = Substitute.For<IPlacesStore>();
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns(saved);
		store.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		var places = new PlacesViewModel(store, ui.Spot, ui.Toast);
		await places.LoadAsync().ConfigureAwait(false);
		return (new PlacePickerViewModel(store, ui.Sender, ui.Spot, places, ui.Toast), places, store);
	}

	/// <summary>
	/// Makes the address search answer with matches.
	/// </summary>
	/// <param name="ui">The fixture that holds the sender.</param>
	/// <param name="matches">The matches to answer with.</param>
	private static void AnswerMatches(UiFixture ui, params AddressMatch[] matches)
	{
		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new SearchAddressResponse(matches)));
	}

	/// <summary>
	/// Starts the picker and searches, so that it ends in the given matches.
	/// </summary>
	/// <param name="ui">The fixture that holds the sender.</param>
	/// <param name="picker">The picker.</param>
	/// <param name="matches">The matches the search finds.</param>
	/// <returns>A task that completes when the search ended.</returns>
	private static async Task SearchAsync(UiFixture ui, PlacePickerViewModel picker, params AddressMatch[] matches)
	{
		AnswerMatches(ui, matches);
		picker.Start(PlaceKind.Home);
		picker.SearchText = " grand ";
		await picker.SearchAsync().ConfigureAwait(false);
	}
	#endregion

	#region Start Tests
	/// <summary>
	/// Verifies that starting with no saved place waits for a search, with nothing chosen and nothing saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_PlaceNotSet_ShouldWaitForASearch()
	{
		using var ui = new UiFixture();
		var (picker, _, store) = await ArrangeAsync(ui);

		picker.Start(PlaceKind.Work);

		picker.State.Should().Be(PlacePickerState.Searching);
		picker.IsActive.Should().BeTrue();
		picker.Title.Should().Be("Set work");
		picker.Candidate.Should().BeNull();
		picker.CanSave.Should().BeFalse();
		picker.IsConfirmShown.Should().BeFalse();
		await store.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
	}

	/// <summary>
	/// Verifies that starting with a saved place shows its pin at once, so that it can be moved or removed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_PlaceSaved_ShouldShowItsPinInTheConfirmStep()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui, new PlaceShortcut(PlaceKind.Home, "Home", Centre, "Grand-Place 1"));

		picker.Start(PlaceKind.Home);

		picker.State.Should().Be(PlacePickerState.Confirming);
		picker.Candidate.Should().Be(Centre);
		picker.CandidateLabel.Should().Be("Grand-Place 1");
		picker.HasSavedPlace.Should().BeTrue();
		picker.Title.Should().Be("Set home");
	}

	/// <summary>
	/// Verifies that a saved place without an address is named by where it came from.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_PlaceSavedFromTheMap_ShouldNameItByItsOrigin()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui, new PlaceShortcut(PlaceKind.Work, "Work", Centre, string.Empty));

		picker.Start(PlaceKind.Work);

		picker.CandidateLabel.Should().Be(PlacePickerViewModel.SavedPositionLabel);
	}
	#endregion

	#region Saving Tests
	/// <summary>
	/// Verifies that starting the picker while a place is being written changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_WhileSaving_ShouldChangeNothing()
	{
		using var ui = new UiFixture();
		var (picker, _, store) = await ArrangeAsync(ui);
		var release = new TaskCompletionSource<Result>();
		store.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(release.Task);
		picker.Start(PlaceKind.Home);
		picker.MoveCandidate(Centre);
		var saving = picker.SaveAsync();

		picker.Start(PlaceKind.Work);

		picker.State.Should().Be(PlacePickerState.Saving);
		picker.Kind.Should().Be(PlaceKind.Home);
		picker.Candidate.Should().Be(Centre);
		release.SetResult(Result.Success());
		await saving;
		picker.IsActive.Should().BeFalse();
	}
	#endregion

	#region Following Tests
	/// <summary>
	/// Verifies that the picker keeps the map still while a followed position moves, and gives the following back on Cancel, with the
	/// following state unchanged all along.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_WhileFollowing_ShouldSuspendTheMapMovesAndResumeOnCancel()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.StartTrackingAsync();
		var (picker, _, _) = await ArrangeAsync(ui);
		var requests = 0;
		ui.Spot.ViewRequested += (_, _) => requests++;

		picker.Start(PlaceKind.Home);
		ui.RaiseFix(ui.FixNow());

		ui.Spot.IsFollowSuspended.Should().BeTrue();
		ui.Spot.IsTracking.Should().BeTrue();
		requests.Should().Be(0);

		picker.Cancel();

		ui.Spot.IsFollowSuspended.Should().BeFalse();
		ui.Spot.IsTracking.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that Save also gives the following back.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_WhileFollowing_ShouldResumeTheFollowing()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);
		picker.Start(PlaceKind.Home);
		picker.MoveCandidate(Centre);

		await picker.SaveAsync();

		ui.Spot.IsFollowSuspended.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that "Use my position" places the candidate once and does not start following.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task UseMyPosition_NotFollowing_ShouldNotStartFollowing()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);
		ui.RaiseFix(ui.FixNow());
		picker.Start(PlaceKind.Home);

		picker.UseMyPosition();

		picker.Candidate.Should().NotBeNull();
		ui.Spot.IsTracking.Should().BeFalse();
		await ui.Tracker.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
	}
	#endregion

	#region Search Tests
	/// <summary>
	/// Verifies that a search with one match goes straight to the confirm step with the pin on it, saving nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_OneMatch_ShouldGoStraightToConfirming()
	{
		using var ui = new UiFixture();
		var (picker, _, store) = await ArrangeAsync(ui);

		await SearchAsync(ui, picker, GrandPlace);

		picker.State.Should().Be(PlacePickerState.Confirming);
		picker.Candidate.Should().Be(Centre);
		picker.CandidateLabel.Should().Be("Grand-Place 1");
		picker.CanSave.Should().BeTrue();
		picker.IsBusy.Should().BeFalse();
		await store.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
	}

	/// <summary>
	/// Verifies that a search with several matches opens the list and chooses none of them.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_SeveralMatches_ShouldShowTheList()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);

		await SearchAsync(ui, picker, GrandPlace, RueNeuve);

		picker.State.Should().Be(PlacePickerState.Results);
		picker.Matches.Should().Equal(GrandPlace, RueNeuve);
		picker.Candidate.Should().BeNull();
		picker.CanSave.Should().BeFalse();
		await ui.Sender.Received(1).Send(Arg.Is<SearchAddressQuery>(query => query.Text == "grand" && query.Limit == SearchAddressQuery.DefaultLimit), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a pick from the list chooses the match and closes the list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Pick_MatchOfTheList_ShouldGoToConfirming()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);
		await SearchAsync(ui, picker, GrandPlace, RueNeuve);

		picker.Pick(RueNeuve);

		picker.State.Should().Be(PlacePickerState.Confirming);
		picker.Candidate.Should().Be(Elsewhere);
		picker.Matches.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that closing the list (the back button) returns to the search and saves nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CloseResults_ListOpen_ShouldReturnToSearching()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);
		await SearchAsync(ui, picker, GrandPlace, RueNeuve);

		picker.CloseResults();

		picker.State.Should().Be(PlacePickerState.Searching);
		picker.Matches.Should().BeEmpty();
		picker.IsActive.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that typing closes the list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SearchText_ChangedWhileListOpen_ShouldCloseTheList()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);
		await SearchAsync(ui, picker, GrandPlace, RueNeuve);

		picker.SearchText = "grand p";

		picker.State.Should().Be(PlacePickerState.Searching);
	}

	/// <summary>
	/// Verifies the messages for an address that was not found and for a search that failed, and that nothing is chosen.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_NotFoundOrFailed_ShouldTellTheUserAndChooseNothing()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);
		await SearchAsync(ui, picker);
		ui.Toast.Message.Should().Be("Address not found.");

		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SearchAddressResponse>(new Error("Nominatim.Timeout", "timed out")));
		await picker.SearchAsync();

		ui.Toast.Message.Should().Be("Search failed — check your connection.");
		picker.State.Should().Be(PlacePickerState.Searching);
		picker.Candidate.Should().BeNull();
		picker.IsBusy.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a blank text sends no search.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_Blank_ShouldSendNothing()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);
		picker.Start(PlaceKind.Home);
		picker.SearchText = "  ";

		await picker.SearchAsync();

		await ui.Sender.DidNotReceive().Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>());
	}
	#endregion

	#region Position And Move Tests
	/// <summary>
	/// Verifies that "Use my position" chooses the position on screen and goes to the confirm step.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task UseMyPosition_PositionKnown_ShouldGoToConfirming()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		var (picker, _, store) = await ArrangeAsync(ui);
		await ui.Spot.HandleMapClickAsync(Centre);
		picker.Start(PlaceKind.Home);

		picker.UseMyPosition();

		picker.State.Should().Be(PlacePickerState.Confirming);
		picker.Candidate.Should().Be(Centre);
		picker.CandidateLabel.Should().Be(PlacePickerViewModel.MyPositionLabel);
		await store.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
	}

	/// <summary>
	/// Verifies that without a position the user is told and nothing is chosen.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task UseMyPosition_NoPosition_ShouldTellTheUser()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);
		picker.Start(PlaceKind.Home);

		picker.UseMyPosition();

		ui.Toast.Message.Should().Be("No position yet.");
		picker.State.Should().Be(PlacePickerState.Searching);
		picker.Candidate.Should().BeNull();
	}

	/// <summary>
	/// Verifies that moving the pin changes the place and its label, and a tap on the map before any choice places the pin.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MoveCandidate_PinDraggedOrMapTapped_ShouldMoveThePinAndRelabel()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);
		picker.Start(PlaceKind.Home);

		picker.MoveCandidate(Elsewhere);
		picker.Candidate.Should().Be(Elsewhere);
		picker.CandidateLabel.Should().Be(PlacePickerViewModel.MovedLabel);
		picker.State.Should().Be(PlacePickerState.Confirming);

		picker.MoveCandidate(Centre);
		picker.Candidate.Should().Be(Centre);
	}

	/// <summary>
	/// Verifies that a move while the picker is closed is ignored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MoveCandidate_PickerClosed_ShouldBeIgnored()
	{
		using var ui = new UiFixture();
		var (picker, _, _) = await ArrangeAsync(ui);

		picker.MoveCandidate(Elsewhere);

		picker.Candidate.Should().BeNull();
		picker.IsActive.Should().BeFalse();
	}
	#endregion

	#region Save Tests
	/// <summary>
	/// Verifies that Save writes exactly once, with the point the user confirmed after dragging, then leaves the picker and tells the user.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_AfterMovingThePin_ShouldSaveOnceWithTheConfirmedPoint()
	{
		using var ui = new UiFixture();
		var (picker, _, store) = await ArrangeAsync(ui);
		await SearchAsync(ui, picker, GrandPlace);
		picker.MoveCandidate(Elsewhere);

		await picker.SaveAsync();
		await picker.SaveAsync();

		await store.Received(1).SavePlaceAsync(PlaceKind.Home, Elsewhere, null, Arg.Any<CancellationToken>());
		picker.IsActive.Should().BeFalse();
		ui.Toast.Message.Should().Be("Home saved.");
	}

	/// <summary>
	/// Verifies that a place chosen from an address is saved with its address in words, and that the places are read again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_AddressChosen_ShouldSaveWithTheDisplayNameAndReloadThePlaces()
	{
		using var ui = new UiFixture();
		var (picker, places, store) = await ArrangeAsync(ui);
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([new PlaceShortcut(PlaceKind.Home, "Home", Centre, "Grand-Place 1, 1000 Brussels")]);
		await SearchAsync(ui, picker, GrandPlace);

		await picker.SaveAsync();

		await store.Received(1).SavePlaceAsync(PlaceKind.Home, Centre, "Grand-Place 1, 1000 Brussels", Arg.Any<CancellationToken>());
		places.Home.Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that Cancel leaves the picker and saves nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Cancel_PlaceChosen_ShouldSaveNothingAndLeave()
	{
		using var ui = new UiFixture();
		var (picker, _, store) = await ArrangeAsync(ui);
		await SearchAsync(ui, picker, GrandPlace);

		picker.Cancel();

		picker.IsActive.Should().BeFalse();
		picker.Candidate.Should().BeNull();
		await store.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
	}

	/// <summary>
	/// Verifies that a place outside the Brussels box cannot be saved, with no write attempted.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_OutsideBrussels_ShouldNotBeAllowed()
	{
		using var ui = new UiFixture();
		var (picker, _, store) = await ArrangeAsync(ui);
		picker.Start(PlaceKind.Home);
		picker.MoveCandidate(Paris);

		await picker.SaveAsync();

		picker.IsOutsideBrussels.Should().BeTrue();
		picker.CanSave.Should().BeFalse();
		picker.IsActive.Should().BeTrue();
		await store.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
	}

	/// <summary>
	/// Verifies that when the domain refuses a place, the user is told and the picker stays on the confirm step.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_DomainRefuses_ShouldShowItsErrorAndStayOpen()
	{
		using var ui = new UiFixture();
		var (picker, _, store) = await ArrangeAsync(ui);
		store.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns(Result.Failure(SavedPlacesErrors.PointOutsideBrussels));
		await SearchAsync(ui, picker, GrandPlace);

		await picker.SaveAsync();

		ui.Toast.Message.Should().Be("Home has to be inside the Brussels-Capital Region.");
		picker.State.Should().Be(PlacePickerState.Confirming);
		picker.Candidate.Should().Be(Centre);
	}

	/// <summary>
	/// Verifies that unreadable saved data and a refusing database are told in the toast and the picker stays open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_StoreThrows_ShouldTellTheUserAndStayOpen()
	{
		using var ui = new UiFixture();
		var (picker, _, store) = await ArrangeAsync(ui);
		await SearchAsync(ui, picker, GrandPlace);
		store.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns<Task<Result>>(_ => throw new SavedDataUnreadableException(SavedDataUnreadableException.PlacesMessage, "Key.Unavailable", isTemporary: true));
		await picker.SaveAsync();
		ui.Toast.Message.Should().Be(SavedDataUnreadableException.PlacesMessage);

		store.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns<Task<Result>>(_ => throw new InvalidOperationException("refused"));
		await picker.SaveAsync();

		ui.Toast.Message.Should().Be(PlacePickerViewModel.WriteFailedText);
		picker.State.Should().Be(PlacePickerState.Confirming);
	}
	#endregion

	#region Remove Tests
	/// <summary>
	/// Verifies that Remove clears the saved place, leaves the picker and tells the user.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_PlaceSaved_ShouldClearItAndLeave()
	{
		using var ui = new UiFixture();
		var (picker, places, store) = await ArrangeAsync(ui, new PlaceShortcut(PlaceKind.Work, "Work", Centre, "Rue Neuve 5"));
		store.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([]);
		picker.Start(PlaceKind.Work);

		await picker.RemoveAsync();

		await store.Received(1).ClearPlaceAsync(PlaceKind.Work, Arg.Any<CancellationToken>());
		places.Work.Should().BeNull();
		picker.IsActive.Should().BeFalse();
		ui.Toast.Message.Should().Be("Work removed.");
	}

	/// <summary>
	/// Verifies that a remove the database refuses is told in the toast and the picker stays open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_DatabaseRefused_ShouldTellTheUserAndStayOpen()
	{
		using var ui = new UiFixture();
		var (picker, _, store) = await ArrangeAsync(ui, new PlaceShortcut(PlaceKind.Work, "Work", Centre, "Rue Neuve 5"));
		store.ClearPlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("refused"));
		picker.Start(PlaceKind.Work);

		await picker.RemoveAsync();

		ui.Toast.Message.Should().Be(PlacePickerViewModel.WriteFailedText);
		picker.IsActive.Should().BeTrue();
	}
	#endregion

	#region Constructor Tests
	/// <summary>
	/// Verifies that the constructor rejects missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Constructor_NullArguments_ShouldThrow()
	{
		using var ui = new UiFixture();
		var (_, places, store) = await ArrangeAsync(ui);

		var noStore = () => new PlacePickerViewModel(null!, ui.Sender, ui.Spot, places, ui.Toast); // null on purpose: proves the guard clause rejects it
		var noSender = () => new PlacePickerViewModel(store, null!, ui.Spot, places, ui.Toast); // null on purpose: proves the guard clause rejects it
		var noSpot = () => new PlacePickerViewModel(store, ui.Sender, null!, places, ui.Toast); // null on purpose: proves the guard clause rejects it
		var noPlaces = () => new PlacePickerViewModel(store, ui.Sender, ui.Spot, null!, ui.Toast); // null on purpose: proves the guard clause rejects it
		var noToast = () => new PlacePickerViewModel(store, ui.Sender, ui.Spot, places, null!); // null on purpose: proves the guard clause rejects it

		noStore.Should().Throw<ArgumentNullException>();
		noSender.Should().Throw<ArgumentNullException>();
		noSpot.Should().Throw<ArgumentNullException>();
		noPlaces.Should().Throw<ArgumentNullException>();
		noToast.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
