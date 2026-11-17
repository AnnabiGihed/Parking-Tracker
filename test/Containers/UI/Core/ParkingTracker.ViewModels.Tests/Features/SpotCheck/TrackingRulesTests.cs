using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="TrackingRules"/>: the age after which a fix is ignored, the moves and delays that call for
///              a new check, the better-accuracy recheck, and when the nearest-spot search runs again.
/// </summary>
public sealed class TrackingRulesTests
{
	#region Static Instances
	/// <summary>
	/// The moment of the tests.
	/// </summary>
	private static readonly DateTimeOffset Now = UiFixture.Start;

	/// <summary>
	/// The place of the last check, which is the place the tests move from.
	/// </summary>
	private static readonly GeoPoint Origin = UiFixture.Here;
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Builds a fix.
	/// </summary>
	/// <param name="metresNorth">How far north of the origin the fix is, in metres.</param>
	/// <param name="accuracy">The accuracy in metres.</param>
	/// <param name="age">How old the fix is.</param>
	/// <returns>The fix.</returns>
	private static GpsFix Fix(double metresNorth = 0, double accuracy = 10, TimeSpan age = default)
	{
		return new GpsFix(new GeoPoint(Origin.Latitude + (metresNorth / 111_320), Origin.Longitude), accuracy, null, Now - age);
	}

	/// <summary>
	/// Builds the record of a check of the user's position.
	/// </summary>
	/// <param name="age">How long ago the check started.</param>
	/// <param name="accuracy">The accuracy of the checked position.</param>
	/// <returns>The record.</returns>
	private static CheckRecord LastCheck(TimeSpan age = default, double accuracy = 10) => new(Origin, Now - age, CheckSource.Me, accuracy);
	#endregion

	#region Stale Fix Tests
	/// <summary>
	/// Verifies the 15 second limit: a fix of exactly 15 seconds is still used, an older one is not.
	/// </summary>
	/// <param name="seconds">The age of the fix in seconds.</param>
	/// <param name="expected">Whether the fix is stale.</param>
	[Theory]
	[InlineData(0, false)]
	[InlineData(15, false)]
	[InlineData(16, true)]
	public void IsStale_FixAge_ShouldIgnoreFixesOlderThanFifteenSeconds(int seconds, bool expected)
	{
		TrackingRules.IsStale(Fix(age: TimeSpan.FromSeconds(seconds)), Now).Should().Be(expected);
	}
	#endregion

	#region Recheck Tests
	/// <summary>
	/// Verifies that the first fix, and a fix after a check of a pin, is checked.
	/// </summary>
	[Fact]
	public void ShouldRecheck_NoCheckYetOrPinChecked_ShouldBeTrue()
	{
		TrackingRules.ShouldRecheck(null, Fix(), Now).Should().BeTrue();
		TrackingRules.ShouldRecheck(new CheckRecord(Origin, Now, CheckSource.Pin, null), Fix(), Now).Should().BeTrue();
	}

	/// <summary>
	/// Verifies the 8 metre rule.
	/// </summary>
	/// <param name="metres">How far the user moved.</param>
	/// <param name="expected">Whether another check follows.</param>
	[Theory]
	[InlineData(3, false)]
	[InlineData(7, false)]
	[InlineData(9, true)]
	public void ShouldRecheck_Move_ShouldCheckFromEightMetres(double metres, bool expected)
	{
		TrackingRules.ShouldRecheck(LastCheck(), Fix(metres), Now).Should().Be(expected);
	}

	/// <summary>
	/// Verifies the 30 second rule for a user who stands still.
	/// </summary>
	/// <param name="seconds">How long ago the last check started.</param>
	/// <param name="expected">Whether another check follows.</param>
	[Theory]
	[InlineData(29, false)]
	[InlineData(30, true)]
	public void ShouldRecheck_StandingStill_ShouldCheckAfterThirtySeconds(int seconds, bool expected)
	{
		TrackingRules.ShouldRecheck(LastCheck(TimeSpan.FromSeconds(seconds)), Fix(), Now).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that the first accurate fix after an inaccurate check is checked at once, and that two inaccurate fixes are not.
	/// </summary>
	[Fact]
	public void ShouldRecheck_AccuracyBecomesGoodEnough_ShouldCheckAtOnce()
	{
		TrackingRules.ShouldRecheck(LastCheck(accuracy: 40), Fix(accuracy: 20), Now).Should().BeTrue();
		TrackingRules.ShouldRecheck(LastCheck(accuracy: 40), Fix(accuracy: 30), Now).Should().BeFalse();
		TrackingRules.ShouldRecheck(LastCheck(accuracy: 20), Fix(accuracy: 10), Now).Should().BeFalse();
	}
	#endregion

	#region Spot Search Tests
	/// <summary>
	/// Verifies when the nearest-spot search runs again: never searched, a pin, after 60 metres or after 60 seconds.
	/// </summary>
	[Fact]
	public void IsSpotSearchDue_Cases_ShouldFollowTheSixtyRule()
	{
		var last = new SpotSearch(Origin, Now);
		var near = new GeoPoint(Origin.Latitude + (30 / 111_320.0), Origin.Longitude);
		var far = new GeoPoint(Origin.Latitude + (70 / 111_320.0), Origin.Longitude);

		TrackingRules.IsSpotSearchDue(null, Origin, Now, CheckSource.Me).Should().BeTrue();
		TrackingRules.IsSpotSearchDue(last, near, Now.AddSeconds(30), CheckSource.Me).Should().BeFalse();
		TrackingRules.IsSpotSearchDue(last, near, Now.AddSeconds(30), CheckSource.Pin).Should().BeTrue();
		TrackingRules.IsSpotSearchDue(last, far, Now.AddSeconds(30), CheckSource.Me).Should().BeTrue();
		TrackingRules.IsSpotSearchDue(last, near, Now.AddSeconds(61), CheckSource.Me).Should().BeTrue();
	}
	#endregion
}
