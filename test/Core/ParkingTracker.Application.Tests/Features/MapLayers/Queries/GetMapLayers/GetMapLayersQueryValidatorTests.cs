using ParkingTracker.Application.Features.MapLayers.Queries.GetMapLayers;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;

namespace ParkingTracker.Application.Tests.Features.MapLayers.Queries.GetMapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="GetMapLayersQueryValidator"/>: an area whose edges are in order.
/// </summary>
public sealed class GetMapLayersQueryValidatorTests
{
	#region Validation Tests
	/// <summary>
	/// Verifies which area edges are accepted.
	/// </summary>
	/// <param name="south">The south latitude.</param>
	/// <param name="north">The north latitude.</param>
	/// <param name="expected">Whether the query is valid.</param>
	[Theory]
	[InlineData(50.8, 50.9, true)]
	[InlineData(50.9, 50.8, false)]
	[InlineData(-91, 50.8, false)]
	public void Validate_AreaEdges_ShouldAcceptOnlyEdgesInOrder(double south, double north, bool expected)
	{
		var result = new GetMapLayersQueryValidator().Validate(new GetMapLayersQuery(south, 4.3, north, 4.4, new LayerSettings()));

		result.IsValid.Should().Be(expected);
	}
	#endregion
}
