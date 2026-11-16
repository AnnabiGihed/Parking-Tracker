using System.Globalization;

namespace ParkingTracker.Domain.Pricing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Writes the paid days and hours and the tariff of a parking rule as the short English texts the app shows,
///              with euro amounts written with a comma.
/// </summary>
public static class TariffText
{
	#region Constants
	/// <summary>
	/// Separator between the parts of the tariff text.
	/// </summary>
	private const string PartSeparator = " · ";

	/// <summary>
	/// Number of days of a Monday to Saturday schedule.
	/// </summary>
	private const int MondayToSaturdayDays = 6;

	/// <summary>
	/// Number of days of a week.
	/// </summary>
	private const int DaysPerWeek = 7;
	#endregion

	#region Static Instances
	/// <summary>
	/// The weekdays in the order the schedule lists them, Monday first and Sunday last, with their short names.
	/// </summary>
	private static readonly (DayOfWeek Day, string Name)[] Weekdays =
	[
		(DayOfWeek.Monday, "Mon"),
		(DayOfWeek.Tuesday, "Tue"),
		(DayOfWeek.Wednesday, "Wed"),
		(DayOfWeek.Thursday, "Thu"),
		(DayOfWeek.Friday, "Fri"),
		(DayOfWeek.Saturday, "Sat"),
		(DayOfWeek.Sunday, "Sun"),
	];
	#endregion

	#region Public Methods
	/// <summary>
	/// Writes the days and hours of a rule: <c>Mon–Sat 09:00–21:00</c>, <c>every day</c> or a list of days.
	/// The hours are written only when both the start and the end are known.
	/// </summary>
	/// <param name="rule">The rule.</param>
	/// <returns>The schedule text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
	public static string Schedule(ParkingRule rule)
	{
		ArgumentNullException.ThrowIfNull(rule);
		var days = Weekdays.Where(weekday => rule.PaidDays.Contains(weekday.Day)).ToList();
		var span = SpanOf(days, rule);
		var hasHours = !string.IsNullOrEmpty(rule.StartHour) && rule.EndHour is not null;
		return hasHours
			? $"{span} {PaidHours.Truncate(rule.StartHour, PaidHours.HourTextLength)}–{PaidHours.Truncate(rule.EndHour, PaidHours.HourTextLength)}"
			: span;
	}

	/// <summary>
	/// Writes the short tariff description: the price or <c>Blue disc</c>, the free minutes, the maximum stay, the schedule and the fine.
	/// </summary>
	/// <param name="rule">The rule, or <see langword="null"/>.</param>
	/// <returns>For example <c>€3,60/h · first 20 min free · max 4 h 30 · Mon–Sat 09:00–21:00 · fine €47,00</c>; <see langword="null"/> for no rule.</returns>
	public static string? Describe(ParkingRule? rule)
	{
		if (rule is null)
		{
			return null;
		}

		var parts = new List<string>();
		if (rule.Type == ZoneType.Disk)
		{
			parts.Add("Blue disc");
		}
		else if (rule.Charge60 is { } hourly)
		{
			parts.Add($"{Euro(hourly)}/h");
		}

		if (rule.FreeTime is { } free and not 0)
		{
			parts.Add($"first {free} min free");
		}

		if (Minutes(rule.MaxTime) is { } maximum)
		{
			parts.Add($"max {maximum}");
		}

		parts.Add(Schedule(rule));
		if (rule.Fee is { } fee)
		{
			parts.Add($"fine {Euro(fee)}");
		}

		return string.Join(PartSeparator, parts);
	}

	/// <summary>
	/// Writes an amount in euros with two decimals and a comma. The amounts come from the Brussels Mobility API as decimal
	/// numbers with at most two decimals, which are written unchanged; an amount with more decimals is rounded to two with
	/// the framework's rounding for decimals (midpoints away from zero, not to even).
	/// </summary>
	/// <param name="amount">The amount.</param>
	/// <returns>For example <c>€3,60</c>.</returns>
	public static string Euro(decimal amount)
	{
		return "€" + amount.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');
	}
	#endregion

	#region Private Helpers
	private static string SpanOf(List<(DayOfWeek Day, string Name)> days, ParkingRule rule)
	{
		if (days.Count == MondayToSaturdayDays && !rule.PaidDays.Contains(DayOfWeek.Sunday))
		{
			return "Mon–Sat";
		}

		return days.Count == DaysPerWeek ? "every day" : string.Join(", ", days.Select(weekday => weekday.Name));
	}

	private static string? Minutes(int? minutes)
	{
		return minutes switch
		{
			null or 0 => null,
			{ } total when total % BrusselsTime.MinutesPerHour == 0 => $"{total / BrusselsTime.MinutesPerHour} h",
			{ } total when total > BrusselsTime.MinutesPerHour => $"{total / BrusselsTime.MinutesPerHour} h {total % BrusselsTime.MinutesPerHour}",
			{ } total => $"{total} min",
		};
	}
	#endregion
}
