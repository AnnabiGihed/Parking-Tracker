using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.ViewModels.Tests.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CoverageVibration"/>: the two patterns of <c>app.js</c>.
/// </summary>
public sealed class CoverageVibrationTests
{
	#region Pattern Tests
	/// <summary>
	/// Verifies that a position that became covered gives two short pulses.
	/// </summary>
	[Fact]
	public void PatternFor_Covered_ShouldBeTwoShortPulses()
	{
		CoverageVibration.PatternFor(true).Should().Equal(80, 60, 80);
	}

	/// <summary>
	/// Verifies that a position that became not covered gives one long pulse.
	/// </summary>
	[Fact]
	public void PatternFor_NotCovered_ShouldBeOneLongPulse()
	{
		CoverageVibration.PatternFor(false).Should().Equal(300);
	}
	#endregion
}
