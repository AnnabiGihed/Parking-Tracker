using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;

namespace ParkingTracker.Application.Tests.Features.ParkingDetection.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="TimerRestorer"/>: the alarms of the saved timer are replaced by those still ahead, nothing
///              is scheduled without a timer or after its end, and the saved timer itself is left alone.
/// </summary>
public sealed class TimerRestorerTests
{
	#region Static Instances
	/// <summary>
	/// The moment the tests start at.
	/// </summary>
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
	#endregion

	#region Fields
	/// <summary>
	/// The settings substitute that holds the timer.
	/// </summary>
	private readonly ISettingsStore settings = Substitute.For<ISettingsStore>();

	/// <summary>
	/// The alarm scheduler substitute.
	/// </summary>
	private readonly IAlarmScheduler alarms = Substitute.For<IAlarmScheduler>();

	/// <summary>
	/// The restorer under test, over a clock fixed at <see cref="Now"/>.
	/// </summary>
	private readonly TimerRestorer restorer;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="TimerRestorerTests"/> class.
	/// </summary>
	public TimerRestorerTests()
	{
		restorer = new TimerRestorer(settings, alarms, new FakeTimeProvider(Now));
	}
	#endregion

	#region Restore Tests
	/// <summary>
	/// Verifies that a running timer gets its two alarms again, after the old ones were cancelled so that nothing is doubled.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Restore_TimerRunning_ShouldCancelThenScheduleBothReminders()
	{
		settings.GetTimerAsync().Returns(new ParkingTimer(Now.AddMinutes(60), 60));

		var scheduled = await restorer.RestoreAsync();

		scheduled.Should().BeTrue();
		Received.InOrder(() =>
		{
			_ = alarms.CancelAsync(Arg.Any<CancellationToken>());
			_ = alarms.ScheduleAsync(Arg.Is<IReadOnlyList<TimerReminder>>(plan => plan.Count == 2), Arg.Any<CancellationToken>());
		});
		await settings.DidNotReceive().ClearTimerAsync();
	}

	/// <summary>
	/// Verifies that only the end is scheduled when the warning moment has passed already.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Restore_WarningPassed_ShouldScheduleOnlyTheEnd()
	{
		settings.GetTimerAsync().Returns(new ParkingTimer(Now.AddMinutes(5), 60));

		await restorer.RestoreAsync();

		await alarms.Received(1).ScheduleAsync(
			Arg.Is<IReadOnlyList<TimerReminder>>(plan => plan.Count == 1 && plan[0].Id == ParkingTimer.EndId),
			Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that nothing is scheduled or cancelled when no timer is saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Restore_NoTimer_ShouldDoNothing()
	{
		var scheduled = await restorer.RestoreAsync();

		scheduled.Should().BeFalse();
		await alarms.DidNotReceiveWithAnyArgs().CancelAsync();
		await alarms.DidNotReceiveWithAnyArgs().ScheduleAsync(default!);
	}

	/// <summary>
	/// Verifies that a timer whose end has passed schedules nothing and is kept, so that the screen still says the time is over.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Restore_TimerEnded_ShouldScheduleNothingAndKeepTheTimer()
	{
		settings.GetTimerAsync().Returns(new ParkingTimer(Now.AddMinutes(-1), 30));

		var scheduled = await restorer.RestoreAsync();

		scheduled.Should().BeFalse();
		await alarms.DidNotReceiveWithAnyArgs().ScheduleAsync(default!);
		await settings.DidNotReceive().ClearTimerAsync();
	}

	/// <summary>
	/// Verifies that missing arguments are rejected.
	/// </summary>
	[Fact]
	public void Constructor_NullArguments_ShouldThrowArgumentNullException()
	{
		var noSettings = () => new TimerRestorer(null!, alarms, TimeProvider.System); // null on purpose: proves the guard clause rejects it
		var noAlarms = () => new TimerRestorer(settings, null!, TimeProvider.System); // null on purpose: proves the guard clause rejects it
		var noClock = () => new TimerRestorer(settings, alarms, null!); // null on purpose: proves the guard clause rejects it

		noSettings.Should().Throw<ArgumentNullException>();
		noAlarms.Should().Throw<ArgumentNullException>();
		noClock.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
