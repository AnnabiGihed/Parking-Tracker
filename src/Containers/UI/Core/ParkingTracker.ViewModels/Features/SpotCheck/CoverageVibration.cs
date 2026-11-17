namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The vibration the first version of the app gave when the user's position changed from not covered to covered or
///              back (<c>navigator.vibrate</c> in <c>app.js</c>): two short pulses for covered, one long pulse for not covered.
///              The pattern is plain data, so that the host only plays it.
/// </summary>
public static class CoverageVibration
{
	#region Public Methods
	/// <summary>
	/// Gets the pattern for a change of coverage.
	/// </summary>
	/// <param name="isCovered"><see langword="true"/> when the position is covered by the card now.</param>
	/// <returns>Milliseconds that alternate between vibrating and pausing, starting with vibrating.</returns>
	public static IReadOnlyList<int> PatternFor(bool isCovered)
	{
		return isCovered ? [80, 60, 80] : [300];
	}
	#endregion
}
