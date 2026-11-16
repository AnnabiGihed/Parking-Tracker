namespace ParkingTracker.Domain.Pricing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Belgian public holidays, on which paid parking usually does not apply: the fixed days plus Easter Monday,
///              Ascension and Whit Monday.
/// </summary>
public static class BelgianHolidays
{
	#region Constants
	/// <summary>
	/// Days from Easter Sunday to Easter Monday.
	/// </summary>
	private const int EasterMondayOffset = 1;

	/// <summary>
	/// Days from Easter Sunday to Ascension Thursday.
	/// </summary>
	private const int AscensionOffset = 39;

	/// <summary>
	/// Days from Easter Sunday to Whit Monday.
	/// </summary>
	private const int WhitMondayOffset = 50;
	#endregion

	#region Static Instances
	/// <summary>
	/// The holidays that fall on the same month and day every year, as (month, day): New Year, Labour Day, National Day,
	/// Assumption, All Saints, Armistice and Christmas.
	/// </summary>
	private static readonly (int Month, int Day)[] FixedDays = [(1, 1), (5, 1), (7, 21), (8, 15), (11, 1), (11, 11), (12, 25)];
	#endregion

	#region Public Methods
	/// <summary>
	/// Computes Easter Sunday of a year with the anonymous Gregorian algorithm (Meeus/Jones/Butcher).
	/// </summary>
	/// <param name="year">The year.</param>
	/// <returns>The date of Easter Sunday.</returns>
	public static DateOnly EasterSunday(int year)
	{
		var a = year % 19;
		var b = year / 100;
		var c = year % 100;
		var d = b / 4;
		var e = b % 4;
		var f = (b + 8) / 25;
		var g = (b - f + 1) / 3;
		var h = ((19 * a) + b - d - g + 15) % 30;
		var i = c / 4;
		var k = c % 4;
		var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
		var m = (a + (11 * h) + (22 * l)) / 451;
		var month = (h + l - (7 * m) + 114) / 31;
		var day = ((h + l - (7 * m) + 114) % 31) + 1;
		return new DateOnly(year, month, day);
	}

	/// <summary>
	/// Determines whether a date is a Belgian public holiday.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns><see langword="true"/> on a fixed holiday, Easter Monday, Ascension or Whit Monday.</returns>
	public static bool IsHoliday(DateOnly date)
	{
		if (FixedDays.Contains((date.Month, date.Day)))
		{
			return true;
		}

		var easter = EasterSunday(date.Year);
		return date == easter.AddDays(EasterMondayOffset) || date == easter.AddDays(AscensionOffset) || date == easter.AddDays(WhitMondayOffset);
	}
	#endregion
}
