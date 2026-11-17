using System.Globalization;
using System.Text.RegularExpressions;
using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Zones.ValueObjects;

namespace ParkingTracker.Domain.Features.PaidHours.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Answers whether paying (or a disc) is required at an instant under a parking rule, and when it starts again,
///              on the clocks of Brussels and with the Belgian public holidays.
/// </summary>
public static class PaidHours
{
	#region Constants
	/// <summary>
	/// How many days ahead <see cref="NextPaid"/> looks, today included.
	/// </summary>
	private const int LookAheadDays = 9;

	/// <summary>
	/// Number of characters of <c>HH:mm</c> kept from an hour such as <c>09:00:00</c>.
	/// </summary>
	internal const int HourTextLength = 5;
	#endregion

	#region Static Instances
	/// <summary>
	/// A time of day such as <c>09:00</c> or <c>09:00:00</c>.
	/// </summary>
	private static readonly Regex TimeOfDay = new("^(?<hours>[0-9]{1,2}):(?<minutes>[0-9]{2})", RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);
	#endregion

	#region Public Methods
	/// <summary>
	/// Determines whether paying (or a disc) is required at an instant. An unknown rule means yes: the app never pretends it is free.
	/// </summary>
	/// <param name="rule">The rule of the zone, or <see langword="null"/> when unknown.</param>
	/// <param name="instant">The instant to check; use <c>TimeProvider.GetUtcNow()</c> for now.</param>
	/// <returns>
	/// <see langword="false"/> for a free zone, on a day the rule does not apply (public holidays included unless the rule applies
	/// on them) and outside the paid hours; otherwise <see langword="true"/>, also when the hours are unknown.
	/// </returns>
	public static bool IsPaidAt(ParkingRule? rule, DateTimeOffset instant)
	{
		if (rule is null)
		{
			return true;
		}

		if (rule.Type == ZoneType.Free)
		{
			return false;
		}

		var time = BrusselsTime.At(instant);
		if (!AppliesOn(rule, time))
		{
			return false;
		}

		var start = Minutes(rule.StartHour);
		var end = Minutes(rule.EndHour);
		return start is null || end is null || (time.MinuteOfDay >= start && time.MinuteOfDay < end);
	}

	/// <summary>
	/// Finds when paying starts next, within the next 9 days. The days are stepped on the Brussels calendar, so "tomorrow"
	/// is right around the daylight-saving changes, where a day lasts 23 or 25 hours.
	/// </summary>
	/// <param name="rule">The rule of the zone, or <see langword="null"/>.</param>
	/// <param name="instant">The instant to start from.</param>
	/// <returns>
	/// A label such as <c>today 09:00</c>, <c>tomorrow 09:00</c> or <c>Monday 09:00</c>; <see langword="null"/> when the rule
	/// has no start hour or no paid day comes within 9 days.
	/// </returns>
	public static string? NextPaid(ParkingRule? rule, DateTimeOffset instant)
	{
		var start = Minutes(rule?.StartHour);
		if (rule is null || start is null)
		{
			return null;
		}

		var now = BrusselsTime.At(instant);
		for (var offset = 0; offset < LookAheadDays; offset++)
		{
			var date = now.Date.AddDays(offset);
			var time = new BrusselsTime(date, date.DayOfWeek, 0);
			if (!AppliesOn(rule, time) || (offset == 0 && now.MinuteOfDay >= start))
			{
				continue;
			}

			var when = offset switch
			{
				0 => "today",
				1 => "tomorrow",
				_ => time.Day.ToString(),
			};
			return $"{when} {Truncate(rule.StartHour, HourTextLength)}";
		}

		return null;
	}

	/// <summary>
	/// Determines whether a rule applies on the day of a Brussels time: the weekday is paid and, on a public holiday, the
	/// rule also applies on holidays.
	/// </summary>
	/// <param name="rule">Parking rule whose paid days and hours are tested.</param>
	/// <param name="time">The Brussels time.</param>
	/// <returns><see langword="true"/> when payment can be required on that day.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
	public static bool AppliesOn(ParkingRule rule, BrusselsTime time)
	{
		ArgumentNullException.ThrowIfNull(rule);
		return rule.PaidDays.Contains(time.Day) && (rule.PaidOnHolidays || !BelgianHolidays.IsHoliday(time.Date));
	}
	#endregion

	#region Internal Methods
	/// <summary>
	/// Cuts a text to at most a number of characters.
	/// </summary>
	/// <param name="text">The text, or <see langword="null"/>.</param>
	/// <param name="length">The maximum length.</param>
	/// <returns>The first characters of the text; empty for <see langword="null"/>.</returns>
	internal static string Truncate(string? text, int length)
	{
		var value = text ?? string.Empty;
		return value.Length <= length ? value : value[..length];
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Converts a time of day written as hours and minutes into minutes since midnight.
	/// </summary>
	/// <param name="timeOfDay">The time of day.</param>
	/// <returns>The minutes since midnight, or <see langword="null"/> when the text is empty or not a time.</returns>
	private static int? Minutes(string? timeOfDay)
	{
		var match = TimeOfDay.Match(timeOfDay ?? string.Empty);
		return match.Success
			? (int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture) * BrusselsTime.MinutesPerHour) + int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture)
			: null;
	}
	#endregion
}
