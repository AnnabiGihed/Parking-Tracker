using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.Services;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The rules of the first version of the screen that decide when a fix is used and when the spot is checked again:
///              a fix older than 15 seconds is ignored, a new check follows a move of 8 metres, 30 seconds without a check, or
///              the first accurate fix after an inaccurate one; the nearest-spot search is repeated after 60 metres or 60
///              seconds, and always for a pin. Pure code with no clock of its own, so it can be tested with fixed moments.
/// </summary>
public static class TrackingRules
{
	#region Constants
	/// <summary>
	/// How far the user must move, in metres, for a new check.
	/// </summary>
	public const double RecheckMetres = 8;

	/// <summary>
	/// How far from the last search, in metres, a new nearest-spot search starts.
	/// </summary>
	public const double SpotSearchMetres = 60;
	#endregion

	#region Static Instances
	/// <summary>
	/// The age after which a fix is ignored.
	/// </summary>
	public static readonly TimeSpan MaxFixAge = TimeSpan.FromSeconds(15);

	/// <summary>
	/// How long without a check before the spot is checked again, even when the user did not move.
	/// </summary>
	public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(30);

	/// <summary>
	/// How long after the last search a new nearest-spot search starts.
	/// </summary>
	public static readonly TimeSpan SpotSearchInterval = TimeSpan.FromSeconds(60);
	#endregion

	#region Public Methods
	/// <summary>
	/// Tells whether a fix is too old to be used.
	/// </summary>
	/// <param name="fix">The fix.</param>
	/// <param name="now">The current moment.</param>
	/// <returns><see langword="true"/> when the fix is older than <see cref="MaxFixAge"/>.</returns>
	public static bool IsStale(GpsFix fix, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(fix);
		return now - fix.Time > MaxFixAge;
	}

	/// <summary>
	/// Tells whether a new fix calls for another spot check while the user is followed.
	/// </summary>
	/// <param name="last">The last check, or <see langword="null"/> when there was none.</param>
	/// <param name="fix">The new fix.</param>
	/// <param name="now">The current moment.</param>
	/// <returns><see langword="true"/> when the last check was not of the user's position, the user moved
	/// <see cref="RecheckMetres"/>, <see cref="RecheckInterval"/> passed, or the accuracy became good enough for an answer.</returns>
	public static bool ShouldRecheck(CheckRecord? last, GpsFix fix, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(fix);
		if (last is null || last.Source != CheckSource.Me)
		{
			return true;
		}

		var moved = GeoMath.Distance(fix.Point, last.Point) >= RecheckMetres;
		var old = now - last.Time >= RecheckInterval;
		var better = last.AccuracyMetres > SpotDecider.MaxGpsAccuracyMetres && fix.AccuracyMetres <= SpotDecider.MaxGpsAccuracyMetres;
		return moved || old || better;
	}

	/// <summary>
	/// Tells whether the nearest-spot search should run again after a check that found no free spot here.
	/// </summary>
	/// <param name="last">The last search, or <see langword="null"/> when there was none.</param>
	/// <param name="at">The place that was just checked.</param>
	/// <param name="now">The current moment.</param>
	/// <param name="source">Whether the place is the user's position or a pin; a pin always searches.</param>
	/// <returns><see langword="true"/> when the search is due.</returns>
	public static bool IsSpotSearchDue(SpotSearch? last, GeoPoint at, DateTimeOffset now, CheckSource source)
	{
		return last is null
			|| source == CheckSource.Pin
			|| GeoMath.Distance(at, last.Point) > SpotSearchMetres
			|| now - last.Time > SpotSearchInterval;
	}
	#endregion
}
