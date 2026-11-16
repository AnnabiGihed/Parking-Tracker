using ParkingTracker.Domain.Geometry;

namespace ParkingTracker.Domain.Detection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Recognises that the user has parked: the phone drove faster than 5.5 m/s (about 20 km/h), then stayed
///              within 15 m for 2 minutes, with the last driving fix less than 20 minutes ago. It is fed fixes and asked
///              about the time explicitly, so it needs no clock or timer.
/// </summary>
public sealed class ParkingDetector
{
	#region Constants
	/// <summary>
	/// Speed in metres per second from which the phone counts as driving.
	/// </summary>
	public const double DrivingSpeed = 5.5;

	/// <summary>
	/// Distance in metres from the last fix beyond which the phone counts as having moved.
	/// </summary>
	public const double MovementThreshold = 15;
	#endregion

	#region Static Instances
	/// <summary>
	/// How long the phone must stay still after driving before it counts as parked.
	/// </summary>
	public static readonly TimeSpan StillFor = TimeSpan.FromSeconds(120);

	/// <summary>
	/// How recent the last driving fix must be for the stop to count as parking.
	/// </summary>
	public static readonly TimeSpan DrivingRecency = TimeSpan.FromMinutes(20);
	#endregion

	#region Fields
	private bool drove;
	private GpsFix? lastFix;
	private DateTimeOffset lastMove;
	private DateTimeOffset lastDrive;
	#endregion

	#region Public Methods
	/// <summary>
	/// Records a fix. A missing or negative device speed is replaced by the speed between this fix and the previous one.
	/// </summary>
	/// <param name="fix">The new fix.</param>
	/// <exception cref="ArgumentNullException"><paramref name="fix"/> is <see langword="null"/>.</exception>
	public void Observe(GpsFix fix)
	{
		ArgumentNullException.ThrowIfNull(fix);
		var previous = lastFix;
		var speed = fix.SpeedMetresPerSecond;
		if ((speed is null or < 0) && previous is not null && fix.Time > previous.Time)
		{
			speed = GeoMath.Distance(fix.Point, previous.Point) / (fix.Time - previous.Time).TotalSeconds;
		}

		if (previous is null || GeoMath.Distance(fix.Point, previous.Point) > MovementThreshold)
		{
			lastMove = fix.Time;
		}

		if (speed >= DrivingSpeed)
		{
			drove = true;
			lastDrive = fix.Time;
		}

		lastFix = fix;
	}

	/// <summary>
	/// Finds where the user has parked, without changing the detector. Call <see cref="AcknowledgeParked"/> once the
	/// parking has been handled, so the same stop is not reported again.
	/// </summary>
	/// <param name="now">The current time.</param>
	/// <returns>The last fix, where the car stands, or <see langword="null"/> when the user has not parked.</returns>
	public GpsFix? GetParkedFix(DateTimeOffset now)
	{
		if (!drove || lastFix is null)
		{
			return null;
		}

		if (now - lastMove < StillFor || now - lastDrive >= DrivingRecency)
		{
			return null;
		}

		return lastFix;
	}

	/// <summary>
	/// Forgets that the phone drove, so <see cref="GetParkedFix"/> reports nothing until the phone drives again.
	/// </summary>
	public void AcknowledgeParked()
	{
		drove = false;
	}
	#endregion
}
