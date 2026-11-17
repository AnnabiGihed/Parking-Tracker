namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the periodic look of the <see cref="FixWatchdog"/> decided about the followed position.
/// </summary>
public enum WatchdogVerdict
{
	/// <summary>
	/// Nothing to do: the position is not followed, or the last check is recent enough.
	/// </summary>
	Quiet,

	/// <summary>
	/// The last check is old and the last fix is fresh: the place should be checked again.
	/// </summary>
	Recheck,

	/// <summary>
	/// The last fix is too old to trust: the answer should say that the signal is lost.
	/// </summary>
	SignalLost,
}
