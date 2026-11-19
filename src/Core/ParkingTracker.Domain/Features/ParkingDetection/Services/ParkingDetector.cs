using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.Services;

namespace ParkingTracker.Domain.Features.ParkingDetection.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Recognises that the user has parked after driving faster than 5.5 m/s (about 20 km/h), with the last
///              driving fix less than 20 minutes ago, in one of two ways: the phone stayed within 15 m of the place where
///              it stopped for 2 minutes, or the phone stood still for at least 20 seconds and then, for 3 minutes, never
///              went faster than walking (about 9 km/h), which is the user leaving the car on foot. Distance is measured
///              from the place where the phone stopped, not from the previous fix: fixes arrive every second, so slow
///              traffic never moves 15 m between two of them. It is fed fixes and asked about the time explicitly, so it
///              needs no clock or timer.
/// </summary>
public sealed class ParkingDetector
{
	#region Constants
	/// <summary>
	/// Speed in metres per second from which the phone counts as driving.
	/// </summary>
	public const double DrivingSpeed = 5.5;

	/// <summary>
	/// Speed in metres per second (about 9 km/h) above which the phone is in a moving vehicle rather than on foot.
	/// </summary>
	public const double WalkingSpeed = 2.5;

	/// <summary>
	/// Distance in metres from the place where the phone stopped beyond which the phone counts as having moved.
	/// </summary>
	public const double MovementThreshold = 15;
	#endregion

	#region Static Instances
	/// <summary>
	/// How long the phone must stay still after driving before it counts as parked.
	/// </summary>
	public static readonly TimeSpan StillFor = TimeSpan.FromSeconds(120);

	/// <summary>
	/// How long the phone must stand still for the place to count as a stop of the car the user may have left.
	/// </summary>
	public static readonly TimeSpan MinimumStop = TimeSpan.FromSeconds(20);

	/// <summary>
	/// How long the phone must stay at walking pace or slower after a stop before the user counts as having left the car.
	/// </summary>
	public static readonly TimeSpan LeftCarAfter = TimeSpan.FromMinutes(3);

	/// <summary>
	/// How recent the last driving fix must be for the stop to count as parking.
	/// </summary>
	public static readonly TimeSpan DrivingRecency = TimeSpan.FromMinutes(20);
	#endregion

	#region Fields
	/// <summary>
	/// Whether the device was seen driving.
	/// </summary>
	private bool drove;

	/// <summary>
	/// The last fix received.
	/// </summary>
	private GpsFix? lastFix;

	/// <summary>
	/// The fix where the phone last moved to: it counts as still while it stays within <see cref="MovementThreshold"/> of it.
	/// </summary>
	private GpsFix? stillAnchor;

	/// <summary>
	/// The first place after the last vehicle-speed fix where the phone stood still for <see cref="MinimumStop"/>: where
	/// the car stands when the user walked away from it.
	/// </summary>
	private GpsFix? carStop;

	/// <summary>
	/// The last moment the device drove.
	/// </summary>
	private DateTimeOffset lastDrive;

	/// <summary>
	/// The last moment the device went faster than <see cref="WalkingSpeed"/>.
	/// </summary>
	private DateTimeOffset lastVehicleMove;
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
		var speed = GetSpeed(fix, lastFix);
		if (stillAnchor is null || GeoMath.Distance(fix.Point, stillAnchor.Point) > MovementThreshold)
		{
			RememberCarStop();
			stillAnchor = fix;
		}

		if (speed > WalkingSpeed)
		{
			// Recorded after the stop above, so that a car driving off a red light forgets that stop.
			lastVehicleMove = fix.Time;
			carStop = null;
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
	/// <returns>Where the car stands, or <see langword="null"/> when the user has not parked.</returns>
	public GpsFix? GetParkedFix(DateTimeOffset now)
	{
		if (!drove || lastFix is null || stillAnchor is null || now - lastDrive >= DrivingRecency)
		{
			return null;
		}

		if (now - stillAnchor.Time >= StillFor)
		{
			return lastFix;
		}

		return now - lastVehicleMove >= LeftCarAfter ? carStop : null;
	}

	/// <summary>
	/// Forgets that the phone drove, so <see cref="GetParkedFix"/> reports nothing until the phone drives again.
	/// </summary>
	public void AcknowledgeParked()
	{
		drove = false;
		carStop = null;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Gives the speed of a fix: the device speed, or when missing or negative the speed since the previous fix.
	/// </summary>
	/// <param name="fix">The new fix.</param>
	/// <param name="previous">The previous fix, if any.</param>
	/// <returns>The speed in metres per second, or <see langword="null"/> when it cannot be known.</returns>
	private static double? GetSpeed(GpsFix fix, GpsFix? previous)
	{
		var speed = fix.SpeedMetresPerSecond;
		if ((speed is null or < 0) && previous is not null && fix.Time > previous.Time)
		{
			speed = GeoMath.Distance(fix.Point, previous.Point) / (fix.Time - previous.Time).TotalSeconds;
		}

		return speed;
	}

	/// <summary>
	/// Keeps the place the phone is leaving as the car stop when it stood still there long enough and no car stop is
	/// known since the last vehicle-speed fix. The phone delivers no fix while it stands still, so the stop lasted at least
	/// until the last fix near that place.
	/// </summary>
	private void RememberCarStop()
	{
		if (carStop is null && stillAnchor is not null && lastFix is not null && lastFix.Time - stillAnchor.Time >= MinimumStop)
		{
			carStop = stillAnchor;
		}
	}
	#endregion
}
