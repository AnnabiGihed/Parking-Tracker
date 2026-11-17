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
	#region Static Instances
	/// <summary>
	/// The pattern for a position that became covered by the card: two short pulses. Milliseconds that alternate between
	/// vibrating and pausing, starting with vibrating.
	/// </summary>
	public static readonly IReadOnlyList<int> CoveredPattern = [80, 60, 80];

	/// <summary>
	/// The pattern for a position that became not covered: one long pulse. Milliseconds that alternate between vibrating and
	/// pausing, starting with vibrating.
	/// </summary>
	public static readonly IReadOnlyList<int> NotCoveredPattern = [300];
	#endregion
}
