using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ClockText"/>: the texts of dates and times are written day first and with 24 hours, as in
///              the first version of the app, and an unknown date is written as a question mark.
/// </summary>
public sealed class ClockTextTests
{
	#region Text Tests
	/// <summary>
	/// Verifies the date and time texts.
	/// </summary>
	[Fact]
	public void Text_Moments_ShouldWriteDayFirstAndTwentyFourHours()
	{
		var moment = new DateTimeOffset(2026, 11, 17, 14, 5, 9, TimeSpan.Zero);

		ClockText.Time(moment).Should().Be("14:05:09");
		ClockText.DateTime(moment).Should().Be("17/11/2026 14:05");
		ClockText.Date(new DateOnly(2026, 11, 17)).Should().Be("17/11/2026");
		ClockText.Date(null).Should().Be("?");
	}
	#endregion
}
