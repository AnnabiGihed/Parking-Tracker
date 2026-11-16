using ParkingTracker.Application.Queries;

namespace ParkingTracker.Application.Tests.Queries;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the validators of the decision queries: coordinates in range, accuracy not negative, an area whose
///              edges are in order and a positive spot count.
/// </summary>
public sealed class QueryValidatorTests
{
	#region Decide Spot Tests
	/// <summary>
	/// Verifies which inputs of a spot question are accepted.
	/// </summary>
	/// <param name="latitude">The latitude.</param>
	/// <param name="longitude">The longitude.</param>
	/// <param name="accuracy">The accuracy, or null.</param>
	/// <param name="expected">Whether the query is valid.</param>
	[Theory]
	[InlineData(50.8, 4.36, 5.0, true)]
	[InlineData(50.8, 4.36, null, true)]
	[InlineData(91.0, 4.36, 5.0, false)]
	[InlineData(50.8, -181.0, 5.0, false)]
	[InlineData(50.8, 4.36, -1.0, false)]
	public void DecideSpotQueryValidator_Inputs_ShouldAcceptOnlyValidOnes(double latitude, double longitude, double? accuracy, bool expected)
	{
		new DecideSpotQueryValidator().Validate(new DecideSpotQuery(latitude, longitude, accuracy)).IsValid.Should().Be(expected);
	}
	#endregion

	#region Area and Spot Tests
	/// <summary>
	/// Verifies that an area with reversed edges is rejected.
	/// </summary>
	[Fact]
	public void GetAreaStatusesQueryValidator_ReversedEdges_ShouldBeInvalid()
	{
		var validator = new GetAreaStatusesQueryValidator();

		validator.Validate(new GetAreaStatusesQuery(50.8, 4.3, 50.9, 4.4)).IsValid.Should().BeTrue();
		validator.Validate(new GetAreaStatusesQuery(50.9, 4.3, 50.8, 4.4)).IsValid.Should().BeFalse();
		validator.Validate(new GetAreaStatusesQuery(50.8, 4.4, 50.9, 4.3)).IsValid.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a free-spot search needs at least one spot.
	/// </summary>
	[Fact]
	public void FindFreeSpotsQueryValidator_ZeroCount_ShouldBeInvalid()
	{
		var validator = new FindFreeSpotsQueryValidator();

		validator.Validate(new FindFreeSpotsQuery(50.8, 4.3)).IsValid.Should().BeTrue();
		validator.Validate(new FindFreeSpotsQuery(50.8, 4.3, 0)).IsValid.Should().BeFalse();
	}
	#endregion
}
