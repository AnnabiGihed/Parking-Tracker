using System.Globalization;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Features.Car;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Writes the parking timer the way the first version of the screen did: the time left as <c>1h05</c>, the line above the
///              status block and the text of the chip. Pure code, so that the texts are tested without a screen.
/// </summary>
public static class TimerText
{
	#region Public Methods
	/// <summary>
	/// Writes a time left as hours and two-digit minutes, for example <c>1h05</c>; a time that is up is written as <c>0h00</c>.
	/// </summary>
	/// <param name="left">The time left.</param>
	/// <returns>The text.</returns>
	public static string Left(TimeSpan left)
	{
		var positive = left < TimeSpan.Zero ? TimeSpan.Zero : left;
		return string.Create(CultureInfo.InvariantCulture, $"{(int)positive.TotalHours}h{positive.Minutes:00}");
	}

	/// <summary>
	/// Writes the line above the status block.
	/// </summary>
	/// <param name="timer">The running timer.</param>
	/// <param name="now">The current moment.</param>
	/// <returns>"Parking time left 1h05 · until 11:05", or "Your parking time is over." once the end passed.</returns>
	public static string Line(ParkingTimer timer, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(timer);
		var left = timer.Remaining(now);
		return left > TimeSpan.Zero ? $"Parking time left {Left(left)} · until {ClockText.ShortTime(timer.End)}" : ParkingTimer.EndText;
	}

	/// <summary>
	/// Writes the text of the car chip while a timer runs.
	/// </summary>
	/// <param name="timer">The running timer.</param>
	/// <param name="now">The current moment.</param>
	/// <returns>"1h05 left", or "Time is over" once the end passed.</returns>
	public static string Chip(ParkingTimer timer, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(timer);
		var left = timer.Remaining(now);
		return left > TimeSpan.Zero ? $"{Left(left)} left" : "Time is over";
	}

	/// <summary>
	/// Writes how long a timer was set for, for the message after it started.
	/// </summary>
	/// <param name="minutes">The length in minutes.</param>
	/// <returns>"30 min", "2 h" or "1 h 30 min".</returns>
	public static string Length(int minutes)
	{
		if (minutes < 60)
		{
			return $"{minutes} min";
		}

		var rest = minutes % 60;
		return rest == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {rest} min";
	}
	#endregion
}
