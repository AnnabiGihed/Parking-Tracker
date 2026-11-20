using NSubstitute;
using ParkingTracker.Application.Features.AddressSearch.Abstractions;
using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.AddressSearch.Queries.SearchAddress;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the address search query: the typed text is trimmed before it leaves the phone, every match is
///              returned with the limit asked for, nothing found is a successful answer without a match, a failed search is a failed result, and text that
///              is empty or too long, or a limit outside 1 to 10, is rejected.
/// </summary>
public sealed class SearchAddressQueryHandlerTests
{
	#region Handler Tests
	/// <summary>
	/// Verifies that the trimmed text is searched with the limit and every match is returned in order.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_Text_ShouldSearchTrimmedTextAndReturnEveryMatch()
	{
		var search = Substitute.For<IAddressSearch>();
		var first = new AddressMatch(new GeoPoint(50.85, 4.35), "Grand-Place");
		var second = new AddressMatch(new GeoPoint(1, 1), null);
		search.SearchAsync("Grand-Place", 5, Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<AddressMatch>>([first, second]));

		var result = await new SearchAddressQueryHandler(search).Handle(new SearchAddressQuery("  Grand-Place ", 5), CancellationToken.None);

		result.Value.Matches.Should().Equal(first, second);
	}

	/// <summary>
	/// Verifies that nothing found is a success without a match.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_NothingFound_ShouldSucceedWithoutMatch()
	{
		var search = Substitute.For<IAddressSearch>();
		search.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<AddressMatch>>([]));

		var result = await new SearchAddressQueryHandler(search).Handle(new SearchAddressQuery("zzz"), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.Matches.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a failed search is returned as the failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_SearchFails_ShouldReturnTheFailure()
	{
		var down = new Error("Nominatim.Timeout", "address search timed out");
		var search = Substitute.For<IAddressSearch>();
		search.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<AddressMatch>>(down));

		var result = await new SearchAddressQueryHandler(search).Handle(new SearchAddressQuery("a"), CancellationToken.None);

		result.Error.Should().Be(down);
	}
	#endregion

	#region Validation Tests
	/// <summary>
	/// Verifies which texts are accepted.
	/// </summary>
	/// <param name="text">The typed text.</param>
	/// <param name="expected">Whether the query is valid.</param>
	[Theory]
	[InlineData("Rue Neuve", true)]
	[InlineData("", false)]
	[InlineData("   ", false)]
	public void Validate_Text_ShouldAcceptOnlyRealText(string text, bool expected)
	{
		new SearchAddressQueryValidator().Validate(new SearchAddressQuery(text)).IsValid.Should().Be(expected);
	}

	/// <summary>
	/// Verifies which limits are accepted: 1 to 10.
	/// </summary>
	/// <param name="limit">The number of matches asked for.</param>
	/// <param name="expected">Whether the query is valid.</param>
	[Theory]
	[InlineData(0, false)]
	[InlineData(1, true)]
	[InlineData(8, true)]
	[InlineData(10, true)]
	[InlineData(11, false)]
	[InlineData(-1, false)]
	public void Validate_Limit_ShouldAcceptOneToTen(int limit, bool expected)
	{
		new SearchAddressQueryValidator().Validate(new SearchAddressQuery("Rue Neuve", limit)).IsValid.Should().Be(expected);
	}

	/// <summary>
	/// Verifies that a query without a limit asks for eight matches.
	/// </summary>
	[Fact]
	public void Query_NoLimit_ShouldAskForEight()
	{
		new SearchAddressQuery("Rue Neuve").Limit.Should().Be(8);
	}

	/// <summary>
	/// Verifies that a text longer than the limit is rejected.
	/// </summary>
	[Fact]
	public void Validate_TooLong_ShouldBeRejected()
	{
		var text = new string('a', SearchAddressQueryValidator.MaxLength + 1);

		new SearchAddressQueryValidator().Validate(new SearchAddressQuery(text)).IsValid.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that leading spaces do not count against the limit and that the text is sent trimmed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Validate_PaddedText_ShouldPassAndBeSentTrimmed()
	{
		var query = new SearchAddressQuery(new string(' ', SearchAddressQueryValidator.MaxLength) + "Rue Neuve");
		var search = Substitute.For<IAddressSearch>();
		search.SearchAsync("Rue Neuve", SearchAddressQuery.DefaultLimit, Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<AddressMatch>>([]));

		new SearchAddressQueryValidator().Validate(query).IsValid.Should().BeTrue();
		var result = await new SearchAddressQueryHandler(search).Handle(query, CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		await search.Received(1).SearchAsync("Rue Neuve", SearchAddressQuery.DefaultLimit, Arg.Any<CancellationToken>());
	}
	#endregion
}
