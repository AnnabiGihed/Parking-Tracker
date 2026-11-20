using NSubstitute;
using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SearchViewModel"/>: one found address moves the map close, drops the pin and checks it,
///              several matches open a list that a pick, a change of the text or a close ends, an address that was not found and a
///              failed search are told to the user in the toast, and an empty text sends nothing.
/// </summary>
public sealed class SearchViewModelTests
{
	#region Submit Tests
	/// <summary>
	/// Verifies that one found address moves the map close, drops the pin and checks it, without opening the list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_OneMatch_ShouldZoomDropThePinAndCheck()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		var match = new AddressMatch(new GeoPoint(50.8503, 4.3517), "Grand-Place");
		AnswerMatches(ui, match);
		MapViewRequest? view = null;
		ui.Spot.ViewRequested += (_, e) => view = e.Value;
		ui.Search.Text = "  Grand-Place ";

		await ui.Search.SubmitAsync();

		view.Should().Be(new MapViewRequest(match.Point, 18));
		ui.Spot.Pin.Should().Be(match.Point);
		ui.Search.IsListOpen.Should().BeFalse();
		await ui.Sender.Received(1).Send(Arg.Is<SearchAddressQuery>(q => q.Text == "Grand-Place"), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that several matches open the list and show nothing on the map yet.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_SeveralMatches_ShouldOpenTheListAndShowNothingYet()
	{
		using var ui = new UiFixture();
		var first = new AddressMatch(new GeoPoint(50.85, 4.35), "Rue de la Loi, Bruxelles", "Rue de la Loi", "1000 Bruxelles");
		var second = new AddressMatch(new GeoPoint(50.84, 4.38), "Rue de la Loi, Etterbeek", "Rue de la Loi", "1040 Etterbeek");
		AnswerMatches(ui, first, second);
		var changes = 0;
		ui.Search.Changed += (_, _) => changes++;
		ui.Search.Text = "Rue de la Loi";

		await ui.Search.SubmitAsync();

		ui.Search.IsListOpen.Should().BeTrue();
		ui.Search.Matches.Should().Equal(first, second);
		ui.Spot.Pin.Should().BeNull();
		changes.Should().BeGreaterThan(0);
		await ui.Sender.Received(1).Send(Arg.Is<SearchAddressQuery>(q => q.Limit == SearchAddressQuery.DefaultLimit), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that no match tells the user and opens no list, also after a list was open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_NoMatch_ShouldTellTheUserAndCloseTheList()
	{
		using var ui = new UiFixture();
		AnswerMatches(ui, new AddressMatch(new GeoPoint(1, 1), "A"), new AddressMatch(new GeoPoint(2, 2), "B"));
		ui.Search.Text = "x";
		await ui.Search.SubmitAsync();
		AnswerMatches(ui);

		await ui.Search.SubmitAsync();

		ui.Search.IsListOpen.Should().BeFalse();
		ui.Toast.Message.Should().Be("Address not found.");
		ui.Spot.Pin.Should().BeNull();
	}

	/// <summary>
	/// Verifies the message for a search that failed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_Failed_ShouldTellTheUser()
	{
		using var ui = new UiFixture();
		ui.Search.Text = "zzz";
		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SearchAddressResponse>(new Error("Nominatim.Timeout", "address search timed out")));

		await ui.Search.SubmitAsync();

		ui.Toast.Message.Should().Be("Search failed — check your connection.");
		ui.Spot.Pin.Should().BeNull();
		ui.Search.IsListOpen.Should().BeFalse();
	}
	#endregion

	#region List Tests
	/// <summary>
	/// Verifies that picking a match shows it on the map and closes the list.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Pick_Match_ShouldShowItAndCloseTheList()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		var picked = new AddressMatch(new GeoPoint(50.84, 4.38), "B");
		AnswerMatches(ui, new AddressMatch(new GeoPoint(50.85, 4.35), "A"), picked);
		MapViewRequest? view = null;
		ui.Spot.ViewRequested += (_, e) => view = e.Value;
		ui.Search.Text = "x";
		await ui.Search.SubmitAsync();

		await ui.Search.PickAsync(picked);

		ui.Search.IsListOpen.Should().BeFalse();
		ui.Search.Matches.Should().BeEmpty();
		view.Should().Be(new MapViewRequest(picked.Point, 18));
		ui.Spot.Pin.Should().Be(picked.Point);
	}

	/// <summary>
	/// Verifies that a change of the text and a close asked by the back button or a tap on the map close the list, and that writing
	/// the same text again does not.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task List_TextChangedOrClosed_ShouldClose()
	{
		using var ui = new UiFixture();
		AnswerMatches(ui, new AddressMatch(new GeoPoint(1, 1), "A"), new AddressMatch(new GeoPoint(2, 2), "B"));
		ui.Search.Text = "x";
		await ui.Search.SubmitAsync();
		ui.Search.Text = "x";
		ui.Search.IsListOpen.Should().BeTrue();

		ui.Search.Text = "xy";
		ui.Search.IsListOpen.Should().BeFalse();

		await ui.Search.SubmitAsync();
		ui.Search.IsListOpen.Should().BeTrue();
		ui.Search.CloseList();
		ui.Search.IsListOpen.Should().BeFalse();
	}
	#endregion

	#region Empty Text Tests
	/// <summary>
	/// Verifies that a blank text sends no search.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_BlankText_ShouldSendNothing()
	{
		using var ui = new UiFixture();
		ui.Search.Text = "   ";

		await ui.Search.SubmitAsync();

		await ui.Sender.DidNotReceive().Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>());
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Makes the address search answer with matches.
	/// </summary>
	/// <param name="ui">The fixture that holds the sender.</param>
	/// <param name="matches">The matches to answer with.</param>
	private static void AnswerMatches(UiFixture ui, params AddressMatch[] matches)
	{
		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new SearchAddressResponse(matches)));
	}
	#endregion
}
