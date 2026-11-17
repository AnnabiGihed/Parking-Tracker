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
/// Purpose     : Unit tests for <see cref="SearchViewModel"/>: a found address moves the map close, drops the pin and checks it, an
///              address that was not found and a failed search are told to the user in the toast, and an empty text sends nothing.
/// </summary>
public sealed class SearchViewModelTests
{
	#region Submit Tests
	/// <summary>
	/// Verifies that a found address moves the map close, drops the pin and checks it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_AddressFound_ShouldZoomDropThePinAndCheck()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		var match = new AddressMatch(new GeoPoint(50.8503, 4.3517), "Grand-Place");
		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new SearchAddressResponse(match)));
		MapViewRequest? view = null;
		ui.Spot.ViewRequested += (_, e) => view = e.Value;
		ui.Search.Text = "  Grand-Place ";

		await ui.Search.SubmitAsync();

		view.Should().Be(new MapViewRequest(match.Point, 18));
		ui.Spot.Pin.Should().Be(match.Point);
		await ui.Sender.Received(1).Send(Arg.Is<SearchAddressQuery>(q => q.Text == "Grand-Place"), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies the messages for an address that was not found and for a search that failed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_NotFoundOrFailed_ShouldTellTheUser()
	{
		using var ui = new UiFixture();
		ui.Search.Text = "zzz";
		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new SearchAddressResponse(null)));
		await ui.Search.SubmitAsync();
		ui.Toast.Message.Should().Be("Address not found.");

		ui.Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SearchAddressResponse>(new Error("Nominatim.Timeout", "address search timed out")));
		await ui.Search.SubmitAsync();

		ui.Toast.Message.Should().Be("Search failed — check your connection.");
		ui.Spot.Pin.Should().BeNull();
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
}
