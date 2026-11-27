using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.ViewModels.Features.ProfileSync;

namespace ParkingTracker.ViewModels.Tests.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests of <see cref="ProfileSyncTrigger"/> (ADR-0032): a change of the user starts a sync two seconds after the last change and a
///              burst of changes starts one, the start, the return to the front and the return of the network start one at once, and a trigger that
///              was disposed starts nothing.
/// </summary>
public sealed class ProfileSyncTriggerTests
{
	#region Test Infrastructure
	/// <summary>
	/// The sync that is asked.
	/// </summary>
	private readonly IProfileSync sync = Substitute.For<IProfileSync>();

	/// <summary>
	/// The tracker that raises the event of a committed change.
	/// </summary>
	private readonly IProfileChangeTracker tracker = Substitute.For<IProfileChangeTracker>();

	/// <summary>
	/// The clock of the delay.
	/// </summary>
	private readonly FakeTimeProvider clock = new();

	/// <summary>
	/// Raises the event of a committed change.
	/// </summary>
	private void Change() => tracker.Changed += Raise.Event();

	/// <summary>
	/// Moves the clock and lets the continuation of the delay run.
	/// </summary>
	/// <param name="by">How far to move it.</param>
	/// <returns>A task that completes when the continuation had its turn.</returns>
	private async Task AdvanceAsync(TimeSpan by)
	{
		clock.Advance(by);
		await Task.Delay(50).ConfigureAwait(false);
	}
	#endregion

	#region Debounce Tests
	/// <summary>
	/// Verifies that a change starts no sync before two seconds have passed and one sync after.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Change_ShouldStartASyncTwoSecondsLater()
	{
		using var trigger = new ProfileSyncTrigger(sync, tracker, clock);

		Change();
		await AdvanceAsync(TimeSpan.FromMilliseconds(1999));
		sync.DidNotReceive().Request();
		await AdvanceAsync(TimeSpan.FromMilliseconds(1));

		sync.Received(1).Request();
	}

	/// <summary>
	/// Verifies that a burst of changes starts one sync, two seconds after the last of them.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Change_Burst_ShouldStartOneSyncAfterTheLastChange()
	{
		using var trigger = new ProfileSyncTrigger(sync, tracker, clock);

		Change();
		await AdvanceAsync(TimeSpan.FromSeconds(1.5));
		Change();
		await AdvanceAsync(TimeSpan.FromSeconds(1.5));
		Change();
		await AdvanceAsync(TimeSpan.FromSeconds(1.9));
		sync.DidNotReceive().Request();
		await AdvanceAsync(TimeSpan.FromSeconds(0.1));

		sync.Received(1).Request();
	}

	/// <summary>
	/// Verifies that a change after a sync starts the next one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Change_AfterASync_ShouldStartAnother()
	{
		using var trigger = new ProfileSyncTrigger(sync, tracker, clock);

		Change();
		await AdvanceAsync(TimeSpan.FromSeconds(2));
		Change();
		await AdvanceAsync(TimeSpan.FromSeconds(2));

		sync.Received(2).Request();
	}

	/// <summary>
	/// Verifies that a trigger that was disposed starts nothing for a pending change or a new one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Dispose_ShouldCancelThePendingChangeAndStopListening()
	{
		var trigger = new ProfileSyncTrigger(sync, tracker, clock);
		Change();

		trigger.Dispose();
		Change();
		await AdvanceAsync(TimeSpan.FromSeconds(5));

		sync.DidNotReceive().Request();
	}
	#endregion

	#region Immediate Tests
	/// <summary>
	/// Verifies that the start, the return to the front and the return of the network start a sync at once.
	/// </summary>
	[Fact]
	public void RequestNow_ShouldStartASyncAtOnce()
	{
		using var trigger = new ProfileSyncTrigger(sync, tracker, clock);

		trigger.RequestNow();

		sync.Received(1).Request();
	}

	/// <summary>
	/// Verifies that missing arguments are rejected.
	/// </summary>
	[Fact]
	public void Constructor_NullArguments_ShouldThrowArgumentNullException()
	{
		var noSync = () => new ProfileSyncTrigger(null!, tracker, clock); // null on purpose: proves the guard clause rejects it
		var noTracker = () => new ProfileSyncTrigger(sync, null!, clock); // null on purpose: proves the guard clause rejects it
		var noClock = () => new ProfileSyncTrigger(sync, tracker, null!); // null on purpose: proves the guard clause rejects it

		noSync.Should().Throw<ArgumentNullException>();
		noTracker.Should().Throw<ArgumentNullException>();
		noClock.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
