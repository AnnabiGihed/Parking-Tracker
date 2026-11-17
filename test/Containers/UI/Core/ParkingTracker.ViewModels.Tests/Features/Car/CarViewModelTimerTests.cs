using NSubstitute;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Car;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the parking alerts switch and the parking timer of <see cref="CarViewModel"/>: the switch is saved and
///              starts or stops the service, the timer asks for the notification permission, replaces and schedules the reminders,
///              falls back to inexact alarms with a pointer to the system screen, stops and is forgotten with the car, its texts
///              follow the clock, and the form starts with the maximum stay of the zone when it is known.
/// </summary>
public sealed class CarViewModelTimerTests
{
	#region Alerts Tests
	/// <summary>
	/// Verifies that switching the alerts on saves them, asks for the notification permission and applies the service.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SetAlerts_On_ShouldSaveAskPermissionAndApplyTheService()
	{
		using var ui = new UiFixture();
		ui.AlertCoordinator.ApplyAsync(Arg.Any<CancellationToken>()).Returns(true);

		await ui.Car.SetAlertsAsync(true);

		ui.Car.AlertsEnabled.Should().BeTrue();
		await ui.Settings.Received(1).SaveAlertsEnabledAsync(true);
		await ui.Notifier.Received(1).RequestPermissionAsync(Arg.Any<CancellationToken>());
		await ui.AlertCoordinator.Received(1).ApplyAsync(Arg.Any<CancellationToken>());
		ui.Toast.Message.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that switching the alerts on without the location "all the time" tells the user what is missing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SetAlerts_OnWithoutBackgroundLocation_ShouldTellTheUser()
	{
		using var ui = new UiFixture();
		ui.AlertCoordinator.ApplyAsync(Arg.Any<CancellationToken>()).Returns(false);

		await ui.Car.SetAlertsAsync(true);

		ui.Toast.Message.Should().Be("Allow location “all the time” so alerts also work in the background");
	}

	/// <summary>
	/// Verifies that switching the alerts off saves them, stops the service and does not ask for a permission or complain.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SetAlerts_Off_ShouldSaveAndStopTheServiceQuietly()
	{
		using var ui = new UiFixture();

		await ui.Car.SetAlertsAsync(false);

		ui.Car.AlertsEnabled.Should().BeFalse();
		await ui.Settings.Received(1).SaveAlertsEnabledAsync(false);
		await ui.Notifier.DidNotReceive().RequestPermissionAsync(Arg.Any<CancellationToken>());
		await ui.AlertCoordinator.Received(1).ApplyAsync(Arg.Any<CancellationToken>());
		ui.Toast.Message.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that the saved switch and timer are read when loaded.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Load_SavedSwitchAndTimer_ShouldReadThem()
	{
		var saved = new ParkingTimer(UiFixture.Start.AddMinutes(30), 30);
		using var ui = new UiFixture(settings =>
		{
			settings.GetAlertsEnabledAsync().Returns(false);
			settings.GetTimerAsync().Returns(saved);
		});

		await ui.Car.LoadAsync();

		ui.Car.AlertsEnabled.Should().BeFalse();
		ui.Car.Timer.Should().Be(saved);
		ui.Car.ChipVisible.Should().BeTrue();
	}
	#endregion

	#region Timer Tests
	/// <summary>
	/// Verifies that starting the timer replaces the old reminders, schedules the warning and the end, saves the timer and says so.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StartTimer_TwoHours_ShouldScheduleSaveAndTellTheUser()
	{
		using var ui = new UiFixture();

		var started = await ui.Car.StartTimerAsync();

		started.Should().BeTrue();
		var expected = new ParkingTimer(UiFixture.Start.AddMinutes(120), 120);
		ui.Car.Timer.Should().Be(expected);
		await ui.Alarms.Received(1).CancelAsync(Arg.Any<CancellationToken>());
		await ui.Alarms.Received(1).ScheduleAsync(Arg.Is<IReadOnlyList<TimerReminder>>(plan => plan.Count == 2), Arg.Any<CancellationToken>());
		await ui.Settings.Received(1).SaveTimerAsync(expected);
		ui.Toast.Message.Should().Be("Timer set: 2 h");
	}

	/// <summary>
	/// Verifies that the minutes of the form are limited to 5 when the timer starts.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StartTimer_MinutesBelowMinimum_ShouldStartFiveMinutes()
	{
		using var ui = new UiFixture();
		ui.Car.SetMinutes(1);

		await ui.Car.StartTimerAsync();

		ui.Car.Timer!.Minutes.Should().Be(5);
		ui.Toast.Message.Should().Be("Timer set: 5 min");
	}

	/// <summary>
	/// Verifies that refused notifications stop the timer before anything is scheduled or saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StartTimer_NotificationsRefused_ShouldNotScheduleAndSay()
	{
		using var ui = new UiFixture();
		ui.Notifier.RequestPermissionAsync(Arg.Any<CancellationToken>()).Returns(false);

		var started = await ui.Car.StartTimerAsync();

		started.Should().BeFalse();
		ui.Car.Timer.Should().BeNull();
		ui.Toast.Message.Should().Be("Allow notifications for the reminder.");
		await ui.Alarms.DidNotReceiveWithAnyArgs().ScheduleAsync(default!, default);
		await ui.Settings.DidNotReceiveWithAnyArgs().SaveTimerAsync(default!);
	}

	/// <summary>
	/// Verifies that when only inexact alarms are possible the timer still runs, the user is told and the system screen opens.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StartTimer_InexactAlarms_ShouldRunAndOpenTheExactAlarmSettings()
	{
		using var ui = new UiFixture();
		ui.Alarms.ScheduleAsync(Arg.Any<IReadOnlyList<TimerReminder>>(), Arg.Any<CancellationToken>()).Returns(AlarmPrecision.Inexact);

		var started = await ui.Car.StartTimerAsync();

		started.Should().BeTrue();
		ui.Car.Timer.Should().NotBeNull();
		ui.Toast.Message.Should().Be("Timer set: 2 h. Allow exact alarms so the reminders come on time.");
		await ui.Alarms.Received(1).OpenExactAlarmSettingsAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that stopping the timer forgets it and cancels the reminders.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StopTimer_RunningTimer_ShouldForgetItAndCancelTheReminders()
	{
		using var ui = new UiFixture();
		await ui.Car.StartTimerAsync();
		ui.Alarms.ClearReceivedCalls();

		await ui.Car.StopTimerAsync();

		ui.Car.Timer.Should().BeNull();
		ui.Car.TimerLine.Should().BeNull();
		await ui.Settings.Received(1).ClearTimerAsync();
		await ui.Alarms.Received(1).CancelAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that forgetting the car also stops the timer.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Forget_RunningTimer_ShouldStopTheTimerToo()
	{
		using var ui = new UiFixture();
		await ui.Car.StartTimerAsync();

		await ui.Car.ForgetAsync();

		ui.Car.Timer.Should().BeNull();
		await ui.Settings.Received(1).ClearTimerAsync();
	}
	#endregion

	#region Text Tests
	/// <summary>
	/// Verifies that the line and chip follow the clock and the chip turns late 10 minutes before the end.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Texts_ClockAdvances_ShouldFollowTheTimeLeftAndTurnLate()
	{
		using var ui = new UiFixture();
		ui.Car.SetMinutes(60);
		await ui.Car.StartTimerAsync();

		ui.Car.TimerLine.Should().Be("Parking time left 1h00 · until 11:00");
		ui.Car.ChipText.Should().Be("1h00 left");
		ui.Car.ChipIsLate.Should().BeFalse();

		ui.Clock.Advance(TimeSpan.FromMinutes(51));
		ui.Car.ChipText.Should().Be("0h09 left");
		ui.Car.ChipIsLate.Should().BeTrue();

		ui.Clock.Advance(TimeSpan.FromMinutes(10));
		ui.Car.TimerLine.Should().Be("Your parking time is over.");
		ui.Car.ChipText.Should().Be("Time is over");
	}

	/// <summary>
	/// Verifies that without a timer the chip says "My car", is shown only with a car, and is never late.
	/// </summary>
	[Fact]
	public void Chip_NoTimerNoCar_ShouldBeHiddenAndPlain()
	{
		using var ui = new UiFixture();

		ui.Car.ChipText.Should().Be("My car");
		ui.Car.ChipVisible.Should().BeFalse();
		ui.Car.ChipIsLate.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the view model raises its change event every 30 seconds, so that the time left is written again.
	/// </summary>
	[Fact]
	public void Refresh_ThirtySecondsPass_ShouldRaiseChanged()
	{
		using var ui = new UiFixture();
		var changes = 0;
		ui.Car.Changed += (_, _) => changes++;

		ui.Clock.Advance(TimeSpan.FromSeconds(30));

		changes.Should().Be(1);
	}
	#endregion

	#region Default Length Tests
	/// <summary>
	/// Verifies that opening the dialog sets the form to the maximum stay of the zone of the last answer, as the first version did.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_ZoneWithMaximumStay_ShouldStartTheFormWithIt()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(InZoneWithMaximumStay(90));
		await ui.Spot.HandleMapClickAsync(UiFixture.Here);

		ui.Car.Open();

		ui.Car.Minutes.Should().Be(90);
	}

	/// <summary>
	/// Verifies that the form keeps its length when the zone has no maximum stay.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_ZoneWithoutMaximumStay_ShouldKeepTheDefaultLength()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(InZoneWithMaximumStay(null));
		await ui.Spot.HandleMapClickAsync(UiFixture.Here);

		ui.Car.Open();

		ui.Car.Minutes.Should().Be(CarViewModel.DefaultMinutes);
	}

	/// <summary>
	/// Verifies that a running timer keeps the form as it is, so that the maximum stay does not overwrite what the user chose.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_TimerRuns_ShouldNotOverwriteTheForm()
	{
		using var ui = new UiFixture(settings => settings.GetTimerAsync().Returns(new ParkingTimer(UiFixture.Start.AddMinutes(30), 30)));
		ui.AnswerChecks(InZoneWithMaximumStay(90));
		await ui.Spot.HandleMapClickAsync(UiFixture.Here);
		await ui.Car.LoadAsync();
		ui.Car.SetMinutes(45);

		ui.Car.Open();

		ui.Car.Minutes.Should().Be(45);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a decision in a zone whose rule has the given maximum stay.
	/// </summary>
	/// <param name="maximumStay">The maximum stay in minutes, or <see langword="null"/>.</param>
	/// <returns>The decision.</returns>
	private static SpotDecision InZoneWithMaximumStay(int? maximumStay)
	{
		var surface = new ZoneSurface("1", "Avenue Louise", "Louizalaan", "IXELLES", 1, null, new SurfaceShape([]));
		var rule = new ParkingRule(ZoneType.Payable, "rule", new HashSet<DayOfWeek>(), null, null, false, null, null, maximumStay, null);
		return SpotDecision.Create(Verdict.Pay, "Paid.", new ZoneReading(surface, rule, null));
	}
	#endregion
}
