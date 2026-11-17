using ParkingTracker.Application.Features.SpotDecisions.Queries.GetAreaStatuses;

namespace ParkingTracker.Application.Tests.Features.SpotDecisions.Queries.GetAreaStatuses;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="GetAreaStatusesQueryValidator"/>: an area whose edges are in order.
/// </summary>
public sealed class GetAreaStatusesQueryValidatorTests
{
	#region Validation Tests
	/// <summary>
	/// Verifies which area edges are accepted.
	/// </summary>
	/// <param name="south">The south latitude.</param>
	/// <param name="west">The west longitude.</param>
	/// <param name="north">The north latitude.</param>
	/// <param name="east">The east longitude.</param>
	/// <param name="expected">Whether the query is valid.</param>
	[Theory]
	[InlineData(50.8, 4.3, 50.9, 4.4, true)]
	[InlineData(50.9, 4.3, 50.8, 4.4, false)]
	[InlineData(50.8, 4.4, 50.9, 4.3, false)]
	public void Validate_AreaEdges_ShouldAcceptOnlyEdgesInOrder(double south, double west, double north, double east, bool expected)
	{
		var result = new GetAreaStatusesQueryValidator().Validate(new GetAreaStatusesQuery(south, west, north, east));

		result.IsValid.Should().Be(expected);
	}
	#endregion
}
