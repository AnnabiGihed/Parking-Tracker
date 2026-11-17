using System.Globalization;

namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Writes moments and dates the way the first version of the screen did (24-hour clock with seconds, day first),
///              independent of the language of the phone.
/// </summary>
public static class ClockText
{
	#region Public Methods
	/// <summary>
	/// Writes the time of day with seconds, for example <c>14:05:09</c>.
	/// </summary>
	/// <param name="moment">The moment, on the clock of the device.</param>
	/// <returns>The time text.</returns>
	public static string Time(DateTimeOffset moment) => moment.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

	/// <summary>
	/// Writes the time of day without seconds, for example <c>14:05</c>.
	/// </summary>
	/// <param name="moment">The moment, on the clock of the device.</param>
	/// <returns>The time text.</returns>
	public static string ShortTime(DateTimeOffset moment) => moment.ToString("HH:mm", CultureInfo.InvariantCulture);

	/// <summary>
	/// Writes a date as day, month and year, for example <c>17/11/2026</c>.
	/// </summary>
	/// <param name="date">The date, or <see langword="null"/>.</param>
	/// <returns>The date text; <c>?</c> when there is no date.</returns>
	public static string Date(DateOnly? date) => date is { } value ? value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "?";

	/// <summary>
	/// Writes a moment as a short date and time, for example <c>17/11/2026 14:05</c>.
	/// </summary>
	/// <param name="moment">The moment, on the clock of the device.</param>
	/// <returns>The date and time text.</returns>
	public static string DateTime(DateTimeOffset moment) => moment.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
	#endregion
}
