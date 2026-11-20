using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Shared.Features.Places.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Shared.Tests.Features.Places.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the Home, Work and favourite chips, the picker header and the confirm bar: the chips render, a tap on a
///              place that is not set starts the picker, a saved place is shown on the map; the header shows the title, searches,
///              lists several matches and uses the position; the confirm bar shows the label and the hint, saves only on Save,
///              disables Save outside Brussels and removes a saved place; the back button closes the list first and then cancels.
/// </summary>
public sealed class PlaceComponentsTests : UiTestContext
{
	#region Test Infrastructure
	/// <summary>
	/// A saved home.
	/// </summary>
	private static readonly PlaceShortcut SavedHome = new(SavedPlaceId.New(), PlaceKind.Home, "Home", Here, "Rue de la Loi 1");

	/// <summary>
	/// A match with a label and a locality.
	/// </summary>
	private static readonly AddressMatch First = new(Here, "Rue de la Loi 1, 1000 Bruxelles", "Rue de la Loi 1", "1000 Bruxelles");

	/// <summary>
	/// A second match.
	/// </summary>
	private static readonly AddressMatch Second = new(new GeoPoint(50.8467, 4.3525), "Rue de la Loi 2, 1000 Bruxelles", "Rue de la Loi 2", "1000 Bruxelles");

	/// <summary>
	/// Makes the address search answer with matches.
	/// </summary>
	/// <param name="matches">The matches.</param>
	private void AnswerMatches(params AddressMatch[] matches)
	{
		Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new SearchAddressResponse(matches)));
		Places.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
	}

	/// <summary>
	/// Renders the header and types a search that finds matches.
	/// </summary>
	/// <param name="matches">The matches the search finds.</param>
	/// <returns>The rendered header.</returns>
	private async Task<IRenderedComponent<PlacePickerHeader>> SearchAsync(params AddressMatch[] matches)
	{
		AnswerMatches(matches);
		var header = Render<PlacePickerHeader>();
		Resolve<PlacePickerViewModel>().Start(PlaceKind.Home);
		await header.Find("#pickerSearch").InputAsync("loi").ConfigureAwait(false);
		await header.Find("#pickerForm").SubmitAsync().ConfigureAwait(false);
		return header;
	}
	#endregion

	#region Chip Tests
	/// <summary>
	/// Verifies that both chips render without edit buttons while no place is saved, and that a tap starts the picker of that kind.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlaceChips_NothingSaved_ShouldRenderTwoChipsAndStartThePickerOnTap()
	{
		var chips = Render<PlaceChips>();

		chips.Find("#placeHome").TextContent.Should().Contain("Set home");
		chips.Find("#placeWork").TextContent.Should().Contain("Set work");
		chips.FindAll("#placeHomeEdit").Should().BeEmpty();

		await chips.Find("#placeWork").ClickAsync();

		var picker = Resolve<PlacePickerViewModel>();
		picker.IsActive.Should().BeTrue();
		picker.Kind.Should().Be(PlaceKind.Work);
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
	/// Verifies that a saved place reads "Home" and that a tap on it shows the place instead of starting the picker.
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
		Resolve<PlacePickerViewModel>().IsActive.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a saved place gets no edit button: it is changed from the side menu.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlaceChips_HomeSaved_ShouldHaveNoEditButtons()
	{
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([SavedHome]);
		var chips = Render<PlaceChips>();

		await chips.WaitForAssertionAsync(() => chips.Find("#placeHome").TextContent.Should().NotContain("Set"));

		chips.FindAll("#placeHomeEdit").Should().BeEmpty();
		chips.FindAll("#placeWorkEdit").Should().BeEmpty();
		chips.FindAll(".place-edit").Should().BeEmpty();
		chips.FindAll("button").Should().BeEmpty();
	}
	#endregion

	#region Favourite Chip Tests
	/// <summary>
	/// Verifies that each favourite gets a chip after Home and Work, and that a tap shows the place on the map.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlaceChips_FavouritesSaved_ShouldShowThemAfterWorkAndShowThePlaceOnTap()
	{
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([SavedHome, new PlaceShortcut(SavedPlaceId.New(), PlaceKind.Favourite, "Bakery", Here, "Rue 1"), new PlaceShortcut(SavedPlaceId.New(), PlaceKind.Favourite, "Park", Here, string.Empty)]);
		var chips = Render<PlaceChips>();
		await chips.WaitForAssertionAsync(() => chips.FindAll(".place-favourite").Should().HaveCount(2));
		AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		MapViewRequest? view = null;
		Resolve<SpotCheckViewModel>().ViewRequested += (_, e) => view = e.Value;

		await chips.FindAll(".place-favourite")[0].ClickAsync();

		chips.FindAll(".rz-chip").Select(chip => chip.TextContent.Trim()).Should().Equal("Home", "Set work", "Bakery", "Park");
		view.Should().NotBeNull();
		Resolve<PlacePickerViewModel>().IsActive.Should().BeFalse();
	}
	#endregion

	#region Picker Header Tests
	/// <summary>
	/// Verifies that the header shows the title of the kind, a back arrow, "Use my position" and a search box.
	/// </summary>
	[Fact]
	public void PickerHeader_Started_ShouldShowTitleBackSearchAndPosition()
	{
		var header = Render<PlacePickerHeader>();

		Resolve<PlacePickerViewModel>().Start(PlaceKind.Work);

		header.Find("#pickerTitle").TextContent.Should().Be("Set work");
		header.Find("#pickerBack").ClassList.Should().Contain("rz-button");
		header.Find("#pickerHere").TextContent.Should().Contain("Use my position");
		header.Find("#pickerSearch").GetAttribute("placeholder").Should().Be("Search a street or address");
	}

	/// <summary>
	/// Verifies that a submit searches the typed text, closes the keyboard, and that one match goes to the confirm step without saving.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PickerHeader_SearchOneMatch_ShouldChooseItWithoutSaving()
	{
		await SearchAsync(First);

		var picker = Resolve<PlacePickerViewModel>();
		picker.State.Should().Be(PlacePickerState.Confirming);
		picker.Candidate.Should().Be(Here);
		JSInterop.VerifyInvoke("document.activeElement.blur");
		await Places.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
	}

	/// <summary>
	/// Verifies that several matches open the list under the box and that a pick chooses the match and closes the list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PickerHeader_SearchSeveralMatches_ShouldListThemAndPick()
	{
		var header = await SearchAsync(First, Second);

		header.FindAll("#addressResults .address-result").Should().HaveCount(2);
		await header.FindAll("#addressResults .address-result")[1].ClickAsync();

		var picker = Resolve<PlacePickerViewModel>();
		picker.Candidate.Should().Be(Second.Point);
		header.FindAll("#addressResults").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that "Use my position" chooses the position on screen.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PickerHeader_UseMyPosition_ShouldChooseThePositionOnScreen()
	{
		AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await Resolve<SpotCheckViewModel>().HandleMapClickAsync(Here);
		var header = Render<PlacePickerHeader>();
		Resolve<PlacePickerViewModel>().Start(PlaceKind.Home);

		await header.Find("#pickerHere").ClickAsync();

		Resolve<PlacePickerViewModel>().Candidate.Should().Be(Here);
	}

	/// <summary>
	/// Verifies that the back arrow cancels the picker.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PickerHeader_BackArrow_ShouldCancelThePicker()
	{
		var header = Render<PlacePickerHeader>();
		Resolve<PlacePickerViewModel>().Start(PlaceKind.Home);

		await header.Find("#pickerBack").ClickAsync();

		Resolve<PlacePickerViewModel>().IsActive.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the back button of the phone closes the list of matches first and cancels the picker on the second press.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PickerHeader_BackButton_ShouldCloseTheListThenCancel()
	{
		var header = await SearchAsync(First, Second);
		var back = Resolve<IBackNavigation>();
		var picker = Resolve<PlacePickerViewModel>();

		var firstPress = await header.InvokeAsync(back.TryGoBack);

		firstPress.Should().BeTrue();
		await header.WaitForAssertionAsync(() => picker.State.Should().Be(PlacePickerState.Searching));
		picker.IsActive.Should().BeTrue();
		var secondPress = await header.InvokeAsync(back.TryGoBack);
		secondPress.Should().BeTrue();
		await header.WaitForAssertionAsync(() => picker.IsActive.Should().BeFalse());
		back.CanGoBack.Should().BeFalse();
		await Places.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
	}
	#endregion

	#region Confirm Bar Tests
	/// <summary>
	/// Verifies that the confirm bar is empty until a place is chosen, then shows the label, the hint, Cancel and Save.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmBar_PlaceChosen_ShouldShowLabelHintAndButtons()
	{
		var bar = Render<PlaceConfirmBar>();
		var picker = Resolve<PlacePickerViewModel>();
		picker.Start(PlaceKind.Home);
		bar.FindAll("#placeConfirm").Should().BeEmpty();

		await bar.InvokeAsync(() => picker.MoveCandidate(Here));

		bar.Find("#placeConfirmLabel").TextContent.Should().Be(PlacePickerViewModel.MovedLabel);
		bar.Find(".place-confirm-hint").TextContent.Should().Be("Drag the pin to adjust");
		bar.Find("#placeCancel").TextContent.Should().Be("Cancel");
		bar.Find("#placeSave").TextContent.Should().Be("Save");
		bar.Find("#placeSave").HasAttribute("disabled").Should().BeFalse();
		bar.FindAll("#placeRemove").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that Save writes exactly once with the chosen point and leaves the picker, and that Cancel writes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmBar_SaveAndCancel_ShouldSaveOnlyOnSave()
	{
		Places.SavePlaceAsync(Arg.Any<PlaceKind>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		var bar = Render<PlaceConfirmBar>();
		var picker = Resolve<PlacePickerViewModel>();
		picker.Start(PlaceKind.Home);
		await bar.InvokeAsync(() => picker.MoveCandidate(Here));
		await bar.Find("#placeCancel").ClickAsync();
		await Places.DidNotReceiveWithAnyArgs().SavePlaceAsync(default, default, default);
		picker.Start(PlaceKind.Home);
		await bar.InvokeAsync(() => picker.MoveCandidate(Here));

		await bar.Find("#placeSave").ClickAsync();

		await Places.Received(1).SavePlaceAsync(PlaceKind.Home, Here, null, Arg.Any<CancellationToken>());
		picker.IsActive.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a favourite has a required name box limited to 40 characters, that Save adds it with the name, and that Home has no name box.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmBar_Favourite_ShouldRequireANameAndSaveIt()
	{
		Places.AddFavouriteAsync(Arg.Any<string>(), Arg.Any<GeoPoint>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		var bar = Render<PlaceConfirmBar>();
		var picker = Resolve<PlacePickerViewModel>();
		await bar.InvokeAsync(() => picker.Start(PlaceKind.Home));
		await bar.InvokeAsync(() => picker.MoveCandidate(Here));
		bar.FindAll("#placeName").Should().BeEmpty();
		await bar.InvokeAsync(() => picker.Cancel());
		await bar.InvokeAsync(() => picker.Start(PlaceKind.Favourite));
		await bar.InvokeAsync(() => picker.MoveCandidate(Here));

		bar.Find("#placeName").GetAttribute("maxlength").Should().Be("40");
		bar.Find("#placeNameError").TextContent.Should().Contain("name");
		bar.Find("#placeSave").HasAttribute("disabled").Should().BeTrue();
		await bar.Find("#placeName").InputAsync("Bakery");
		bar.FindAll("#placeNameError").Should().BeEmpty();
		bar.Find("#placeSave").HasAttribute("disabled").Should().BeFalse();
		await bar.Find("#placeSave").ClickAsync();

		await Places.Received(1).AddFavouriteAsync("Bakery", Here, null, Arg.Any<CancellationToken>());
		picker.IsActive.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that an edited favourite shows its name and Remove, which removes it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmBar_EditedFavourite_ShouldShowItsNameAndRemoveIt()
	{
		var favourite = new PlaceShortcut(SavedPlaceId.New(), PlaceKind.Favourite, "Bakery", Here, "Rue 1");
		var bar = Render<PlaceConfirmBar>();
		await bar.InvokeAsync(() => Resolve<PlacePickerViewModel>().Edit(favourite));

		bar.Find("#placeName").GetAttribute("value").Should().Be("Bakery");
		await bar.Find("#placeRemove").ClickAsync();

		await Places.Received(1).RemoveFavouriteAsync(favourite.Id, Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a place outside Brussels disables Save and shows the reason.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmBar_OutsideBrussels_ShouldDisableSaveAndSayWhy()
	{
		var bar = Render<PlaceConfirmBar>();
		var picker = Resolve<PlacePickerViewModel>();
		picker.Start(PlaceKind.Home);

		await bar.InvokeAsync(() => picker.MoveCandidate(new GeoPoint(48.8566, 2.3522)));

		bar.Find("#placeSave").HasAttribute("disabled").Should().BeTrue();
		bar.Find("#placeConfirmError").TextContent.Should().Contain("Brussels");
	}

	/// <summary>
	/// Verifies that a saved place is shown with Remove, which clears it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ConfirmBar_PlaceSaved_ShouldOfferRemoveThatClearsThePlace()
	{
		Places.GetPlacesAsync(Arg.Any<CancellationToken>()).Returns([SavedHome]);
		await Resolve<PlacesViewModel>().LoadAsync();
		var bar = Render<PlaceConfirmBar>();
		await bar.InvokeAsync(() => Resolve<PlacePickerViewModel>().Start(PlaceKind.Home));

		await bar.Find("#placeRemove").ClickAsync();

		await Places.Received(1).ClearPlaceAsync(PlaceKind.Home, Arg.Any<CancellationToken>());
	}
	#endregion
}
