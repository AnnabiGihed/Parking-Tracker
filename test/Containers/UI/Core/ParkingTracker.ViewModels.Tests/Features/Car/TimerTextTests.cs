using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.ViewModels.Features.Car;

namespace ParkingTracker.ViewModels.Tests.Features.Car;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="TimerText"/>: the time left, the line, the chip and the length, as the first version wrote them.
/// </summary>
public sealed class TimerTextTests
{
	#region Static Instances
	/// <summary>
	/// The moment the tests start at.
	/// </summary>
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
	#endregion

	#region Left Tests
	/// <summary>
	/// Verifies the hours-and-minutes text, with two digits for the minutes and zero for a time that is up.
	/// </summary>
	/// <param name="minutes">The minutes left.</param>
	/// <param name="expected">The expected text.</param>
	[Theory]
	[InlineData(65, "1h05")]
	[InlineData(9, "0h09")]
	[InlineData(600, "10h00")]
	[InlineData(-3, "0h00")]
	public void Left_Minutes_ShouldWriteHoursAndMinutes(int minutes, string expected)
	{
		TimerText.Left(TimeSpan.FromMinutes(minutes)).Should().Be(expected);
	}
	#endregion

	#region Line and Chip Tests
	/// <summary>
	/// Verifies the line and the chip while time is left.
	/// </summary>
	[Fact]
	public void LineAndChip_TimeLeft_ShouldShowTimeLeftAndEnd()
	{
		var timer = new ParkingTimer(Now.AddMinutes(90), 90);

		TimerText.Line(timer, Now).Should().Be("Parking time left 1h30 · until 11:30");
		TimerText.Chip(timer, Now).Should().Be("1h30 left");
	}

	/// <summary>
	/// Verifies the line and the chip once the end passed.
	/// </summary>
	[Fact]
	public void LineAndChip_EndPassed_ShouldSayTheTimeIsOver()
	{
		var timer = new ParkingTimer(Now.AddMinutes(-1), 90);

		TimerText.Line(timer, Now).Should().Be("Your parking time is over.");
		TimerText.Chip(timer, Now).Should().Be("Time is over");
	}
	#endregion

	#region Length Tests
	/// <summary>
	/// Verifies how a length is written.
	/// </summary>
	/// <param name="minutes">The length in minutes.</param>
	/// <param name="expected">The expected text.</param>
	[Theory]
	[InlineData(30, "30 min")]
	[InlineData(60, "1 h")]
	[InlineData(90, "1 h 30 min")]
	[InlineData(180, "3 h")]
	public void Length_Minutes_ShouldWriteMinutesOrHours(int minutes, string expected)
	{
		TimerText.Length(minutes).Should().Be(expected);
	}
	#endregion

	#region Guard Tests
	/// <summary>
	/// Verifies that a missing timer is rejected.
	/// </summary>
	[Fact]
	public void LineAndChip_NullTimer_ShouldThrowArgumentNullException()
	{
		var line = () => TimerText.Line(null!, Now); // null on purpose: proves the guard clause rejects it
		var chip = () => TimerText.Chip(null!, Now); // null on purpose: proves the guard clause rejects it

		line.Should().Throw<ArgumentNullException>();
		chip.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
