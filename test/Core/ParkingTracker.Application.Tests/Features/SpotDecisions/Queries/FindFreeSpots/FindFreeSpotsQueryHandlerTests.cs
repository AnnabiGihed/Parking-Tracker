using NSubstitute;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Domain.Shared;
using static ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles.AreaQueryStubs;

namespace ParkingTracker.Application.Tests.Features.SpotDecisions.Queries.FindFreeSpots;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="FindFreeSpotsQueryHandler"/>: a broken context or a failed area is a failed result,
///              an unusable card finds nothing without fetching, and the search asks for house numbers.
/// </summary>
public sealed class FindFreeSpotsQueryHandlerTests
{
	#region Free Spot Tests
	/// <summary>
	/// Verifies that a context that cannot be built is a failed result.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ContextFails_ShouldReturnTheFailure()
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
	public async Task Handle_NoCard_ShouldFindNothingWithoutFetching()
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
	public async Task Handle_AreaFails_ShouldReturnTheFailure()
	{
		var areas = Areas(Failed());
		var handler = new FindFreeSpotsQueryHandler(Contexts(ValidContext()), areas, new FixedClock());

		var result = await handler.Handle(new FindFreeSpotsQuery(50.825, 4.3606), CancellationToken.None);

		result.Error.Should().Be(Down);
		await areas.Received(1).LoadAsync(Arg.Any<DecisionContext>(), Arg.Any<GeoBox>(), Arg.Any<DateTimeOffset>(), AddressFetching.Fetch, Arg.Any<CancellationToken>());
	}
	#endregion
}
