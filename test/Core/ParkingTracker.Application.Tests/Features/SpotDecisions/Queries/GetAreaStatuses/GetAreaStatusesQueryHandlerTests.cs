using NSubstitute;
using ParkingTracker.Application.Features.SpotDecisions.Queries.GetAreaStatuses;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Domain.Shared;
using static ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles.AreaQueryStubs;

namespace ParkingTracker.Application.Tests.Features.SpotDecisions.Queries.GetAreaStatuses;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="GetAreaStatusesQueryHandler"/>: a broken context or a failed area is a failed result
///              and the address option is passed on to the provider.
/// </summary>
public sealed class GetAreaStatusesQueryHandlerTests
{
	#region Area Status Tests
	/// <summary>
	/// Verifies that a context that cannot be built is a failed result and nothing is fetched.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ContextFails_ShouldReturnTheFailure()
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
	public async Task Handle_AreaFails_ShouldReturnTheFailure()
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
	public async Task Handle_AddressOption_ShouldBePassedToTheProvider(bool withAddresses, AddressFetching expected)
	{
		var areas = Areas(Result.Success<IReadOnlyList<SurfaceStatus>>([]));
		var handler = new GetAreaStatusesQueryHandler(Contexts(ValidContext()), areas, new FixedClock());

		await handler.Handle(new GetAreaStatusesQuery(50.82, 4.35, 50.83, 4.37, withAddresses), CancellationToken.None);

		await areas.Received(1).LoadAsync(Arg.Any<DecisionContext>(), Arg.Any<GeoBox>(), Arg.Any<DateTimeOffset>(), expected, Arg.Any<CancellationToken>());
	}
	#endregion
}
