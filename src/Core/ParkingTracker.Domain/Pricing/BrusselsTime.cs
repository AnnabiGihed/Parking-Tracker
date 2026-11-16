namespace ParkingTracker.Domain.Pricing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : An instant seen on the clocks of Brussels, whatever the time zone of the phone.
/// </summary>
/// <param name="Date">The calendar date in Brussels.</param>
/// <param name="Day">The weekday in Brussels.</param>
/// <param name="MinuteOfDay">Minutes since midnight in Brussels.</param>
public readonly record struct BrusselsTime(DateOnly Date, DayOfWeek Day, int MinuteOfDay)
{
	#region Constants
	/// <summary>
	/// Number of minutes in an hour.
	/// </summary>
	internal const int MinutesPerHour = 60;

	/// <summary>
	/// IANA identifier of the Brussels time zone, known on Android, Linux, macOS and current Windows.
	/// </summary>
	private const string IanaZoneId = "Europe/Brussels";

	/// <summary>
	/// Windows identifier of the same time zone, tried when the platform does not know the IANA identifier.
	/// </summary>
	private const string WindowsZoneId = "Romance Standard Time";
	#endregion

	#region Static Instances
	/// <summary>
	/// The Brussels time zone: found by its IANA identifier, otherwise by its Windows identifier. When the platform knows
	/// neither, using this type fails with an <see cref="InvalidOperationException"/> rather than guessing an offset.
	/// </summary>
	public static readonly TimeZoneInfo Zone = ResolveZone();
	#endregion

	#region Factory Methods
	/// <summary>
	/// Converts an instant to Brussels time.
	/// </summary>
	/// <param name="instant">The instant.</param>
	/// <returns>The date, weekday and minute of the day in Brussels.</returns>
	public static BrusselsTime At(DateTimeOffset instant)
	{
		var local = TimeZoneInfo.ConvertTime(instant, Zone);
		return new BrusselsTime(DateOnly.FromDateTime(local.DateTime), local.DayOfWeek, (local.Hour * MinutesPerHour) + local.Minute);
	}
	#endregion

	#region Private Helpers
	private static TimeZoneInfo ResolveZone()
	{
		if (TimeZoneInfo.TryFindSystemTimeZoneById(IanaZoneId, out var zone) || TimeZoneInfo.TryFindSystemTimeZoneById(WindowsZoneId, out zone))
		{
			return zone;
		}

		throw new InvalidOperationException($"The time zone '{IanaZoneId}' ('{WindowsZoneId}' on Windows) is not available on this device.");
	}
	#endregion
}
