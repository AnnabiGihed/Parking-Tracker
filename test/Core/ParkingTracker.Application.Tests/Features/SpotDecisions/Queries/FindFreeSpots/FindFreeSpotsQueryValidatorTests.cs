using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;

namespace ParkingTracker.Application.Tests.Features.SpotDecisions.Queries.FindFreeSpots;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="FindFreeSpotsQueryValidator"/>: a positive spot count.
/// </summary>
public sealed class FindFreeSpotsQueryValidatorTests
{
	#region Validation Tests
	/// <summary>
	/// Verifies that a search with the default spot count is accepted.
	/// </summary>
	[Fact]
	public void Validate_DefaultCount_ShouldBeValid()
	{
		var result = new FindFreeSpotsQueryValidator().Validate(new FindFreeSpotsQuery(50.8, 4.3));

		result.IsValid.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a free-spot search needs at least one spot.
	/// </summary>
	[Fact]
	public void Validate_ZeroCount_ShouldBeInvalid()
	{
		var result = new FindFreeSpotsQueryValidator().Validate(new FindFreeSpotsQuery(50.8, 4.3, 0));

		result.IsValid.Should().BeFalse();
	}
	#endregion
}
