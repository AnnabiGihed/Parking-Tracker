namespace ParkingTracker.Domain.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Text helpers that reproduce how the first version of the app wrote numbers and dates in its sentences.
/// </summary>
internal static class JsText
{
	#region Internal Methods
	/// <summary>
	/// Rounds half up, as the first version did, where <see cref="Math.Round(double)"/> rounds half to even.
	/// </summary>
	/// <param name="value">Number to round.</param>
	/// <returns>The rounded value as a whole number.</returns>
	internal static long Round(double value) => (long)Math.Floor(value + 0.5);

	/// <summary>
	/// Writes an ISO date <c>yyyy-MM-dd</c> as <c>dd/MM/yyyy</c>.
	/// </summary>
	/// <param name="isoDate">The date, or <see langword="null"/>.</param>
	/// <returns>The date as day, month and year; <c>?</c> for no date.</returns>
	internal static string FormatDate(string? isoDate)
	{
		return string.IsNullOrEmpty(isoDate) ? "?" : string.Join('/', isoDate.Split('-').Reverse());
	}
	#endregion
}
