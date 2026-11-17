using NSubstitute;
using ParkingTracker.Application.Features.Sectors.Queries.GetOfficialListStatus;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Domain.Shared;
using static ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles.AreaQueryStubs;

namespace ParkingTracker.Application.Tests.Features.Sectors.Queries.GetOfficialListStatus;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="GetOfficialListStatusQueryHandler"/>: it returns the status the decision context holds,
///              none for a municipality without street lists, and the failure of a context that cannot be built.
/// </summary>
public sealed class GetOfficialListStatusQueryHandlerTests
{
	#region Status Tests
	/// <summary>
	/// Verifies that the status of the context is returned.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ContextWithStatus_ShouldReturnIt()
	{
		var status = OfficialListStatus.ChangedDocument("https://example.org/list.pdf", "01/11/2026", new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
		var context = Result.Success(new DecisionContext(null, Etterbeek, null, status));

		var result = await new GetOfficialListStatusQueryHandler(Contexts(context), new FixedClock()).Handle(new GetOfficialListStatusQuery(), CancellationToken.None);

		result.Value.Status.Should().Be(status);
	}

	/// <summary>
	/// Verifies that a municipality without street lists has no status.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_NoStreetLists_ShouldReturnNoStatus()
	{
		var result = await new GetOfficialListStatusQueryHandler(Contexts(ValidContext()), new FixedClock()).Handle(new GetOfficialListStatusQuery(), CancellationToken.None);

		result.Value.Status.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a context that cannot be built is a failed result.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ContextFails_ShouldReturnTheFailure()
	{
		var result = await new GetOfficialListStatusQueryHandler(Contexts(Result.Failure<DecisionContext>(Down)), new FixedClock()).Handle(new GetOfficialListStatusQuery(), CancellationToken.None);

		result.Error.Should().Be(Down);
	}
	#endregion
}
