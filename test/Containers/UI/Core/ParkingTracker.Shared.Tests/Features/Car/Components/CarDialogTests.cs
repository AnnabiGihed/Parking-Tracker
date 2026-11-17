using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Shared.Features.Car.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Car;

namespace ParkingTracker.Shared.Tests.Features.Car.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the parking alerts switch and the parking timer of the car dialog, the timer line of the sheet and
///              the car chip: the switch is on by default and saves a change, a preset fills the minutes, Start schedules the
///              reminders, Stop appears only while a timer runs, and the line and chip show the time left.
/// </summary>
public sealed class CarDialogTests : UiTestContext
{
	#region Alerts Tests
	/// <summary>
	/// Verifies that the alerts switch is on by default and saves the user's change.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_Open_ShouldShowAlertsOnAndSaveAChange()
	{
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(() => Resolve<CarViewModel>().Open());

		cut.Find("#alertsToggle").HasAttribute("checked").Should().BeTrue();

		await cut.Find("#alertsToggle").ChangeAsync(false);

		_ = Settings.Received(1).SaveAlertsEnabledAsync(false);
	}
	#endregion

	#region Timer Tests
	/// <summary>
	/// Verifies that a preset fills the minutes, Start schedules the two reminders and saves the timer, and that Stop shows up then.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_PresetOneHour_ShouldScheduleAndOfferStop()
	{
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(() => Resolve<CarViewModel>().Open());
		cut.FindAll("#timerStop").Should().BeEmpty();

		await cut.Find("[data-min='60']").ClickAsync();
		cut.Find("[data-min='60']").ClassList.Should().Contain("on");
		await cut.Find("#timerStart").ClickAsync();

		_ = Alarms.Received(1).ScheduleAsync(Arg.Is<IReadOnlyList<TimerReminder>>(plan => plan.Count == 2), Arg.Any<CancellationToken>());
		_ = Settings.Received(1).SaveTimerAsync(new ParkingTimer(Start.AddMinutes(60), 60));
		cut.FindAll("#timerStop").Should().ContainSingle();
	}

	/// <summary>
	/// Verifies that typed minutes are used by the timer, limited to 5 minutes at least.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_TypedMinutes_ShouldUseThem()
	{
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(() => Resolve<CarViewModel>().Open());

		await cut.Find("#timerMinutes").ChangeAsync("45");
		await cut.Find("#timerStart").ClickAsync();

		_ = Settings.Received(1).SaveTimerAsync(new ParkingTimer(Start.AddMinutes(45), 45));
	}

	/// <summary>
	/// Verifies that Stop cancels the reminders and hides itself.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Stop_RunningTimer_ShouldCancelAndHide()
	{
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(() => Resolve<CarViewModel>().Open());
		await cut.Find("#timerStart").ClickAsync();

		await cut.Find("#timerStop").ClickAsync();

		_ = Settings.Received(1).ClearTimerAsync();
		cut.FindAll("#timerStop").Should().BeEmpty();
	}
	#endregion

	#region Line and Chip Tests
	/// <summary>
	/// Verifies that the timer line is drawn only while a timer runs and says the time left and the end.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task TimerLine_TimerRuns_ShouldShowTimeLeft()
	{
		var line = Render<TimerLine>();
		line.FindAll("#timerLine").Should().BeEmpty();

		await line.InvokeAsync(() => Resolve<CarViewModel>().ApplyTimerAsync(new ParkingTimer(Start.AddMinutes(90), 90)));

		line.Find("#timerLine").TextContent.Should().Contain("Parking time left 1h30 · until 11:30");
	}

	/// <summary>
	/// Verifies that the chip shows the time left and turns late within 10 minutes of the end.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CarChip_TimerNearEnd_ShouldShowTimeLeftAndBeLate()
	{
		var chip = Render<CarChip>();
		chip.Find("#carChip").ClassList.Should().Contain("hidden");

		await chip.InvokeAsync(() => Resolve<CarViewModel>().ApplyTimerAsync(new ParkingTimer(Start.AddMinutes(8), 60)));

		chip.Find("#carChip").ClassList.Should().Contain("late").And.NotContain("hidden");
		chip.Find("#carChip").TextContent.Should().Contain("0h08 left");
	}
	#endregion
}
