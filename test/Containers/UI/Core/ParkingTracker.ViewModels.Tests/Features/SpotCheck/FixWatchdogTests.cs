using Microsoft.Extensions.Time.Testing;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="FixWatchdog"/>: the judgement of a followed position (quiet, check again, signal lost) and
///              the timer that fires every five seconds, starts once and stops for good when the watchdog is disposed.
/// </summary>
public sealed class FixWatchdogTests
{
	#region Static Instances
	/// <summary>
	/// The moment of the judgements.
	/// </summary>
	private static readonly DateTimeOffset Now = UiFixture.Start.AddMinutes(5);
	#endregion

	#region Judge Tests
	/// <summary>
	/// Verifies that nothing happens when the position is not followed or no fix arrived yet.
	/// </summary>
	[Fact]
	public void Judge_NotFollowingOrNoFix_ShouldBeQuiet()
	{
		FixWatchdog.Judge(false, FreshFix(), null, Now).Should().Be(WatchdogVerdict.Quiet);
		FixWatchdog.Judge(true, null, null, Now).Should().Be(WatchdogVerdict.Quiet);
	}

	/// <summary>
	/// Verifies that nothing happens while the last check is younger than 30 seconds.
	/// </summary>
	[Fact]
	public void Judge_RecentCheck_ShouldBeQuiet()
	{
		var check = new CheckRecord(UiFixture.Here, Now.AddSeconds(-29), CheckSource.Me, 10);

		FixWatchdog.Judge(true, FreshFix(), check, Now).Should().Be(WatchdogVerdict.Quiet);
	}

	/// <summary>
	/// Verifies that an old check with a fresh fix asks for a new check.
	/// </summary>
	[Fact]
	public void Judge_OldCheckFreshFix_ShouldRecheck()
	{
		var check = new CheckRecord(UiFixture.Here, Now.AddSeconds(-30), CheckSource.Me, 10);

		FixWatchdog.Judge(true, FreshFix(), check, Now).Should().Be(WatchdogVerdict.Recheck);
	}

	/// <summary>
	/// Verifies that an old check with a fix older than 15 seconds says the signal is lost.
	/// </summary>
	[Fact]
	public void Judge_OldCheckOldFix_ShouldSayTheSignalIsLost()
	{
		var check = new CheckRecord(UiFixture.Here, Now.AddSeconds(-40), CheckSource.Me, 10);
		var fix = new PositionFix(new GpsFix(UiFixture.Here, 10, null, Now.AddSeconds(-16)), Now.AddSeconds(-16));

		FixWatchdog.Judge(true, fix, check, Now).Should().Be(WatchdogVerdict.SignalLost);
	}
	#endregion

	#region Timer Tests
	/// <summary>
	/// Verifies that the timer fires every five seconds, that starting twice does not add a second timer, and that a disposed
	/// watchdog stays quiet and cannot be started again.
	/// </summary>
	[Fact]
	public void Start_ThenDispose_ShouldTickEveryFiveSecondsUntilDisposed()
	{
		var clock = new FakeTimeProvider(UiFixture.Start);
		var ticks = 0;
		using var watchdog = new FixWatchdog(clock, () => ticks++);

		watchdog.Start();
		watchdog.Start();
		clock.Advance(TimeSpan.FromSeconds(10));
		watchdog.Dispose();
		clock.Advance(TimeSpan.FromSeconds(10));
		watchdog.Start();
		clock.Advance(TimeSpan.FromSeconds(10));

		ticks.Should().Be(2);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a fix that arrived just now.
	/// </summary>
	/// <returns>The fix.</returns>
	private static PositionFix FreshFix() => new(new GpsFix(UiFixture.Here, 10, null, Now), Now);
	#endregion
}
