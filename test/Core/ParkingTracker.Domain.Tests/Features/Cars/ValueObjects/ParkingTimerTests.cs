using ParkingTracker.Domain.Features.Cars.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.Cars.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ParkingTimer"/>: the length is limited as the form of the first version limited it, the
///              reminders are the warning 10 minutes before the end and the end, and one that is too close is not planned.
/// </summary>
public sealed class ParkingTimerTests
{
	#region Static Instances
	/// <summary>
	/// The moment the tests start at.
	/// </summary>
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
	#endregion

	#region StartingAt Tests
	/// <summary>
	/// Verifies that a length is moved into the range 5 minutes to one day, and that the end follows the limited length.
	/// </summary>
	/// <param name="asked">The length asked for.</param>
	/// <param name="expected">The length the timer gets.</param>
	[Theory]
	[InlineData(0, 5)]
	[InlineData(-30, 5)]
	[InlineData(4, 5)]
	[InlineData(120, 120)]
	[InlineData(5000, 1440)]
	public void StartingAt_Minutes_ShouldLimitTheLength(int asked, int expected)
	{
		var timer = ParkingTimer.StartingAt(asked, Now);

		timer.Minutes.Should().Be(expected);
		timer.End.Should().Be(Now.AddMinutes(expected));
	}
	#endregion

	#region Plan Tests
	/// <summary>
	/// Verifies that a long timer plans the warning and the end with the ids and texts of the first version.
	/// </summary>
	[Fact]
	public void Plan_LongTimer_ShouldPlanWarningAndEnd()
	{
		var timer = ParkingTimer.StartingAt(60, Now);

		var plan = timer.Plan(Now);

		plan.Should().Equal(
			new TimerReminder(4101, Now.AddMinutes(50), "Your parking time ends in 10 minutes."),
			new TimerReminder(4102, Now.AddMinutes(60), "Your parking time is over."));
	}

	/// <summary>
	/// Verifies that a timer shorter than 10 minutes has no warning, because it would be due already.
	/// </summary>
	[Fact]
	public void Plan_ShortTimer_ShouldPlanOnlyTheEnd()
	{
		var timer = new ParkingTimer(Now.AddMinutes(1), 1);

		timer.Plan(Now).Should().ContainSingle().Which.Id.Should().Be(ParkingTimer.EndId);
	}

	/// <summary>
	/// Verifies that a reminder less than 5 seconds away is left out.
	/// </summary>
	[Fact]
	public void Plan_EndInThreeSeconds_ShouldPlanNothing()
	{
		var timer = new ParkingTimer(Now.AddSeconds(3), 1);

		timer.Plan(Now).Should().BeEmpty();
	}
	#endregion

	#region Remaining Tests
	/// <summary>
	/// Verifies the time left and that the timer is late from 10 minutes before the end, also after it.
	/// </summary>
	/// <param name="minutesLeft">Minutes left at the moment of the check.</param>
	/// <param name="late">Whether the timer counts as late.</param>
	[Theory]
	[InlineData(11, false)]
	[InlineData(10, true)]
	[InlineData(0, true)]
	[InlineData(-5, true)]
	public void IsLate_MinutesLeft_ShouldBeLateFromTenMinutes(int minutesLeft, bool late)
	{
		var timer = new ParkingTimer(Now.AddMinutes(minutesLeft), 60);

		timer.IsLate(Now).Should().Be(late);
		timer.Remaining(Now).Should().Be(TimeSpan.FromMinutes(minutesLeft));
	}
	#endregion
}
