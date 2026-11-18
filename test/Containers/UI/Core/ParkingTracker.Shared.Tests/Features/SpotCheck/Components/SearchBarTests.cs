using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Shared.Features.SpotCheck.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Shared.Tests.Features.SpotCheck.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for <see cref="SearchBar"/>, built on a Radzen text box: typing fills the view model, a submit
///              looks the text up, an empty text sends nothing, and a failed or empty search tells the user.
/// </summary>
public sealed class SearchBarTests : UiTestContext
{
	#region Search Tests
	/// <summary>
	/// Verifies that typing fills the text of the view model and that the text box keeps its id and placeholder.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Input_Typing_ShouldFillTheViewModelText()
	{
		var cut = Render<SearchBar>();

		await cut.Find("#searchInput").InputAsync("Avenue Louise");

		cut.Find("#searchInput").GetAttribute("placeholder").Should().Be("Search a street or address");
		Resolve<SearchViewModel>().Text.Should().Be("Avenue Louise");
	}

	/// <summary>
	/// Verifies that a submit sends the typed text to the search.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_TypedText_ShouldSearchIt()
	{
		Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new SearchAddressResponse(null)));
		var cut = Render<SearchBar>();
		await cut.Find("#searchInput").InputAsync("Avenue Louise");

		await cut.Find("#searchForm").SubmitAsync();

		await Sender.Received(1).Send(Arg.Is<SearchAddressQuery>(query => query.Text == "Avenue Louise"), Arg.Any<CancellationToken>());
		Resolve<ToastViewModel>().Message.Should().Be("Address not found.");
	}

	/// <summary>
	/// Verifies that an empty text sends no search.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_EmptyText_ShouldNotSearch()
	{
		var cut = Render<SearchBar>();

		await cut.Find("#searchForm").SubmitAsync();

		await Sender.DidNotReceive().Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a failed search tells the user.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_SearchFails_ShouldTellTheUser()
	{
		Sender.Send(Arg.Any<SearchAddressQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SearchAddressResponse>(new Error("Search.Unavailable", "offline")));
		var cut = Render<SearchBar>();
		await cut.Find("#searchInput").InputAsync("Avenue Louise");

		await cut.Find("#searchForm").SubmitAsync();

		Resolve<ToastViewModel>().Message.Should().Be("Search failed — check your connection.");
	}
	#endregion
}
