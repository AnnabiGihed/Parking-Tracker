using System.Globalization;
using ParkingTracker.Domain.Pricing;

namespace ParkingTracker.Domain.Tests.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds parking rules and instants for tests with the values of a typical Brussels paid zone.
/// </summary>
public static class RuleBuilder
{
	#region Static Instances
	/// <summary>
	/// Monday to Saturday.
	/// </summary>
	public static readonly IReadOnlySet<DayOfWeek> MondayToSaturday = new HashSet<DayOfWeek>
	{
		DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
	};
	#endregion

	#region Public Methods
	/// <summary>
	/// Creates a rule that applies Monday to Saturday from 09:00 to 21:00, not on public holidays, with the tariff of the red zones.
	/// </summary>
	/// <param name="type">The zone type of the rule.</param>
	/// <param name="paidDays">The paid weekdays; Monday to Saturday when omitted.</param>
	/// <param name="start">Start hour; <c>09:00:00</c> when omitted.</param>
	/// <param name="end">End hour; <c>21:00:00</c> when omitted.</param>
	/// <param name="paidOnHolidays">Whether the rule applies on public holidays.</param>
	/// <returns>The rule.</returns>
	public static ParkingRule Create(
		ZoneType? type = ZoneType.Payable,
		IReadOnlySet<DayOfWeek>? paidDays = null,
		string? start = "09:00:00",
		string? end = "21:00:00",
		bool paidOnHolidays = false)
	{
		return new ParkingRule(type, null, paidDays ?? MondayToSaturday, start, end, paidOnHolidays, 3.60m, 20, 270, 47.00m);
	}

	/// <summary>
	/// Parses an ISO 8601 instant with an offset.
	/// </summary>
	/// <param name="text">The instant, for example <c>2026-11-16T09:00:00Z</c>.</param>
	/// <returns>The instant.</returns>
	public static DateTimeOffset At(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
	#endregion
}
