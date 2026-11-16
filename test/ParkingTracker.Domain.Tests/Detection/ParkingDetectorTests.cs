using ParkingTracker.Domain.Detection;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Tests.TestDoubles;

namespace ParkingTracker.Domain.Tests.Detection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ParkingDetector"/> with synthetic tracks: <c>parkDetector</c> and the "parked"
///              check of app.js (driving from 5.5 m/s, still for 120 s, last drive within 20 minutes).
/// </summary>
public sealed class ParkingDetectorTests
{
	#region Constants
	private const double Latitude = 50.85;
	private const double Longitude = 4.35;

	/// <summary>
	/// Degrees of latitude that make about 100 m.
	/// </summary>
	private const double HundredMetres = 0.0009;
	#endregion

	#region Static Instances
	private static readonly DateTimeOffset Start = RuleBuilder.At("2026-11-16T10:00:00Z");
	#endregion

	#region Test Infrastructure
	private static GpsFix Fix(int seconds, double speed, double latitude = Latitude)
	{
		return new GpsFix(new GeoPoint(latitude, Longitude), 8, speed, Start.AddSeconds(seconds));
	}

	private static GpsFix FixWithoutSpeed(int seconds, double latitude)
	{
		return new GpsFix(new GeoPoint(latitude, Longitude), 8, null, Start.AddSeconds(seconds));
	}
	#endregion

	#region Parked Tests
	/// <summary>
	/// Verifies that after driving the user is parked once the phone has been still for 120 seconds, not before.
	/// </summary>
	[Fact]
	public void GetParkedFix_DroveThenStill_ShouldReportParkedAfterTwoMinutes()
	{
		var detector = new ParkingDetector();
		detector.Observe(Fix(0, 10));
		var stopped = Fix(30, 0);
		detector.Observe(stopped);

		detector.GetParkedFix(Start.AddSeconds(119)).Should().BeNull();
		detector.GetParkedFix(Start.AddSeconds(120)).Should().BeSameAs(stopped);
	}

	/// <summary>
	/// Verifies that asking twice gives the same answer: the query does not change the detector.
	/// </summary>
	[Fact]
	public void GetParkedFix_AskedTwice_ShouldReportTheSameFix()
	{
		var detector = new ParkingDetector();
		detector.Observe(Fix(0, 10));

		var first = detector.GetParkedFix(Start.AddSeconds(130));

		first.Should().NotBeNull();
		detector.GetParkedFix(Start.AddSeconds(131)).Should().BeSameAs(first);
	}

	/// <summary>
	/// Verifies that after the parking is acknowledged it is not reported again.
	/// </summary>
	[Fact]
	public void GetParkedFix_AfterAcknowledging_ShouldNotReportAgain()
	{
		var detector = new ParkingDetector();
		detector.Observe(Fix(0, 10));
		detector.GetParkedFix(Start.AddSeconds(130)).Should().NotBeNull();

		detector.AcknowledgeParked();

		detector.GetParkedFix(Start.AddSeconds(200)).Should().BeNull();
	}

	/// <summary>
	/// Verifies that driving again after an acknowledged parking makes the next stop count.
	/// </summary>
	[Fact]
	public void GetParkedFix_DrivingAgainAfterAcknowledging_ShouldReportTheNextStop()
	{
		var detector = new ParkingDetector();
		detector.Observe(Fix(0, 10));
		detector.AcknowledgeParked();
		detector.Observe(Fix(300, 10, Latitude + HundredMetres));

		detector.GetParkedFix(Start.AddSeconds(430)).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that moving more than 15 m restarts the two minutes.
	/// </summary>
	[Fact]
	public void GetParkedFix_MovedAfterDriving_ShouldRestartTheStillTime()
	{
		var detector = new ParkingDetector();
		detector.Observe(Fix(0, 10));
		detector.Observe(Fix(60, 0, Latitude + HundredMetres));

		detector.GetParkedFix(Start.AddSeconds(150)).Should().BeNull();
		detector.GetParkedFix(Start.AddSeconds(180)).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a small move of 15 m or less does not count as movement.
	/// </summary>
	[Fact]
	public void GetParkedFix_DriftWithinFifteenMetres_ShouldNotRestartTheStillTime()
	{
		var detector = new ParkingDetector();
		detector.Observe(Fix(0, 10));
		detector.Observe(Fix(60, 0, Latitude + (HundredMetres * 0.1)));

		detector.GetParkedFix(Start.AddSeconds(120)).Should().NotBeNull();
	}
	#endregion

	#region Not Parked Tests
	/// <summary>
	/// Verifies that a phone that never drove is never parked, and that without any fix nothing is reported.
	/// </summary>
	[Fact]
	public void GetParkedFix_NeverDroveOrNoFix_ShouldReturnNull()
	{
		var detector = new ParkingDetector();
		detector.GetParkedFix(Start).Should().BeNull();

		detector.Observe(Fix(0, 1.5));

		detector.GetParkedFix(Start.AddMinutes(10)).Should().BeNull();
	}

	/// <summary>
	/// Verifies that driving 5.4 m/s is not driving and 5.5 m/s is.
	/// </summary>
	[Fact]
	public void GetParkedFix_SpeedAroundTheDrivingThreshold_ShouldCountFromFivePointFive()
	{
		var slow = new ParkingDetector();
		slow.Observe(Fix(0, 5.4));
		var fast = new ParkingDetector();
		fast.Observe(Fix(0, 5.5));

		slow.GetParkedFix(Start.AddSeconds(130)).Should().BeNull();
		fast.GetParkedFix(Start.AddSeconds(130)).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that the last driving fix must be less than 20 minutes ago.
	/// </summary>
	[Fact]
	public void GetParkedFix_LastDriveTwentyMinutesAgo_ShouldNotReport()
	{
		var detector = new ParkingDetector();
		detector.Observe(Fix(0, 10));

		detector.GetParkedFix(Start.AddMinutes(20).AddSeconds(-1)).Should().NotBeNull();

		var late = new ParkingDetector();
		late.Observe(Fix(0, 10));
		late.GetParkedFix(Start.AddMinutes(20)).Should().BeNull();
	}
	#endregion

	#region Derived Speed Tests
	/// <summary>
	/// Verifies that without a device speed the speed between two fixes is used: about 100 m in 5 seconds is driving.
	/// </summary>
	[Fact]
	public void Observe_NoDeviceSpeed_ShouldDeriveTheSpeedFromThePositions()
	{
		var detector = new ParkingDetector();
		detector.Observe(FixWithoutSpeed(0, Latitude));
		detector.Observe(FixWithoutSpeed(5, Latitude + HundredMetres));

		detector.GetParkedFix(Start.AddSeconds(125)).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a negative device speed is replaced the same way.
	/// </summary>
	[Fact]
	public void Observe_NegativeDeviceSpeed_ShouldDeriveTheSpeedFromThePositions()
	{
		var detector = new ParkingDetector();
		detector.Observe(Fix(0, -1));
		detector.Observe(Fix(5, -1, Latitude + HundredMetres));

		detector.GetParkedFix(Start.AddSeconds(125)).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a slow walk without a device speed is not driving, and that a fix that is not newer cannot derive a speed.
	/// </summary>
	[Fact]
	public void Observe_SlowOrNotNewerFixWithoutSpeed_ShouldNotCountAsDriving()
	{
		var walking = new ParkingDetector();
		walking.Observe(FixWithoutSpeed(0, Latitude));
		walking.Observe(FixWithoutSpeed(100, Latitude + HundredMetres));
		var sameTime = new ParkingDetector();
		sameTime.Observe(FixWithoutSpeed(0, Latitude));
		sameTime.Observe(FixWithoutSpeed(0, Latitude + HundredMetres));

		walking.GetParkedFix(Start.AddSeconds(300)).Should().BeNull();
		sameTime.GetParkedFix(Start.AddSeconds(300)).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a missing fix is refused.
	/// </summary>
	[Fact]
	public void Observe_Null_ShouldThrow()
	{
		var act = () => new ParkingDetector().Observe(null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
