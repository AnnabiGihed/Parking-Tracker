using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;

namespace ParkingTracker.Application.Tests.Features.SpotDecisions.Queries.DecideSpot;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="DecideSpotQueryValidator"/>: coordinates in range and accuracy not negative.
/// </summary>
public sealed class DecideSpotQueryValidatorTests
{
	#region Validation Tests
	/// <summary>
	/// Verifies which inputs of a spot question are accepted.
	/// </summary>
	/// <param name="latitude">Latitude in degrees.</param>
	/// <param name="longitude">Longitude in degrees.</param>
	/// <param name="accuracy">The accuracy, or null.</param>
	/// <param name="expected">Whether the query is valid.</param>
	[Theory]
	[InlineData(50.8, 4.36, 5.0, true)]
	[InlineData(50.8, 4.36, null, true)]
	[InlineData(91.0, 4.36, 5.0, false)]
	[InlineData(50.8, -181.0, 5.0, false)]
	[InlineData(50.8, 4.36, -1.0, false)]
	public void Validate_Inputs_ShouldAcceptOnlyValidOnes(double latitude, double longitude, double? accuracy, bool expected)
	{
		new DecideSpotQueryValidator().Validate(new DecideSpotQuery(latitude, longitude, accuracy)).IsValid.Should().Be(expected);
	}
	#endregion
}
