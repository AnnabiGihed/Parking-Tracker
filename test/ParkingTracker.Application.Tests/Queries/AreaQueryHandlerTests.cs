using NSubstitute;
using ParkingTracker.Application.Decisions;
using ParkingTracker.Application.Queries;
using ParkingTracker.Application.Tests.TestDoubles;
using ParkingTracker.Domain.Cards;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.ReferenceData;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Queries;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="GetAreaStatusesQueryHandler"/> and <see cref="FindFreeSpotsQueryHandler"/>: a broken context
///              or a failed area is a failed result, an unusable card finds nothing without fetching, and the address option is passed on.
/// </summary>
public sealed class AreaQueryHandlerTests
{
	#region Test Infrastructure
	private static readonly Error Down = new("Test.Down", "source down");

	private static readonly Municipality Etterbeek = new("etterbeek", "Etterbeek", "Etterbeek", "ETTERBEEK", MunicipalityKind.Whole, [], [], null);

	private static IDecisionContextFactory Contexts(Result<DecisionContext> result)
	{
		var contexts = Substitute.For<IDecisionContextFactory>();
		contexts.CreateAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(result);
		return contexts;
	}

	private static Result<DecisionContext> ValidContext()
	{
		return Result.Success(new DecisionContext(new ParkingCard("etterbeek", null, new CardPeriod(null, null), [], [], []), Etterbeek, null, null));
	}

	private static IAreaStatusProvider Areas(Result<IReadOnlyList<SurfaceStatus>> result)
	{
		var areas = Substitute.For<IAreaStatusProvider>();
		areas.LoadAsync(Arg.Any<DecisionContext>(), Arg.Any<GeoBox>(), Arg.Any<DateTimeOffset>(), Arg.Any<AddressFetching>(), Arg.Any<CancellationToken>()).Returns(result);
		return areas;
	}

	private static Result<IReadOnlyList<SurfaceStatus>> Failed() => Result.Failure<IReadOnlyList<SurfaceStatus>>(Down);
	#endregion

	#region Area Status Tests
	/// <summary>
	/// Verifies that a context that cannot be built is a failed result and nothing is fetched.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAreaStatuses_ContextFails_ShouldReturnTheFailure()
	{
		var areas = Areas(Failed());
		var handler = new GetAreaStatusesQueryHandler(Contexts(Result.Failure<DecisionContext>(Down)), areas, new FixedClock());

		var result = await handler.Handle(new GetAreaStatusesQuery(50.82, 4.35, 50.83, 4.37), CancellationToken.None);

		result.Error.Should().Be(Down);
		await areas.DidNotReceiveWithAnyArgs().LoadAsync(default!, default, default, default, default);
	}

	/// <summary>
	/// Verifies that a failed area is returned as the failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAreaStatuses_AreaFails_ShouldReturnTheFailure()
	{
		var handler = new GetAreaStatusesQueryHandler(Contexts(ValidContext()), Areas(Failed()), new FixedClock());

		var result = await handler.Handle(new GetAreaStatusesQuery(50.82, 4.35, 50.83, 4.37), CancellationToken.None);

		result.Error.Should().Be(Down);
	}

	/// <summary>
	/// Verifies that the address option of the query decides whether house numbers are fetched.
	/// </summary>
	/// <param name="withAddresses">The option of the query.</param>
	/// <param name="expected">The option passed to the provider.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(true, AddressFetching.Fetch)]
	[InlineData(false, AddressFetching.Skip)]
	public async Task GetAreaStatuses_AddressOption_ShouldBePassedToTheProvider(bool withAddresses, AddressFetching expected)
	{
		var areas = Areas(Result.Success<IReadOnlyList<SurfaceStatus>>([]));
		var handler = new GetAreaStatusesQueryHandler(Contexts(ValidContext()), areas, new FixedClock());

		await handler.Handle(new GetAreaStatusesQuery(50.82, 4.35, 50.83, 4.37, withAddresses), CancellationToken.None);

		await areas.Received(1).LoadAsync(Arg.Any<DecisionContext>(), Arg.Any<GeoBox>(), Arg.Any<DateTimeOffset>(), expected, Arg.Any<CancellationToken>());
	}
	#endregion

	#region Free Spot Tests
	/// <summary>
	/// Verifies that a context that cannot be built is a failed result.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FindFreeSpots_ContextFails_ShouldReturnTheFailure()
	{
		var handler = new FindFreeSpotsQueryHandler(Contexts(Result.Failure<DecisionContext>(Down)), Areas(Failed()), new FixedClock());

		var result = await handler.Handle(new FindFreeSpotsQuery(50.825, 4.3606), CancellationToken.None);

		result.Error.Should().Be(Down);
	}

	/// <summary>
	/// Verifies that a card that cannot be searched with finds nothing and fetches nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FindFreeSpots_NoCard_ShouldFindNothingWithoutFetching()
	{
		var areas = Areas(Failed());
		var context = Result.Success(new DecisionContext(null, Etterbeek, null, null));
		var handler = new FindFreeSpotsQueryHandler(Contexts(context), areas, new FixedClock());

		var result = await handler.Handle(new FindFreeSpotsQuery(50.825, 4.3606), CancellationToken.None);

		result.Value.Should().BeEmpty();
		await areas.DidNotReceiveWithAnyArgs().LoadAsync(default!, default, default, default, default);
	}

	/// <summary>
	/// Verifies that a failed area is returned as the failure, and that the search asks for house numbers.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FindFreeSpots_AreaFails_ShouldReturnTheFailure()
	{
		var areas = Areas(Failed());
		var handler = new FindFreeSpotsQueryHandler(Contexts(ValidContext()), areas, new FixedClock());

		var result = await handler.Handle(new FindFreeSpotsQuery(50.825, 4.3606), CancellationToken.None);

		result.Error.Should().Be(Down);
		await areas.Received(1).LoadAsync(Arg.Any<DecisionContext>(), Arg.Any<GeoBox>(), Arg.Any<DateTimeOffset>(), AddressFetching.Fetch, Arg.Any<CancellationToken>());
	}
	#endregion
}
