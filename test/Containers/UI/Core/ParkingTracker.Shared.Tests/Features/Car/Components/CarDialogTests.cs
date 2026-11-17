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
/// Purpose     : Component tests for the parking alerts switch and the parking timer of the car dialog: the switch is on by default
///              and saves a change, a preset fills the minutes, Start schedules the reminders, and Stop appears only while a timer
///              runs.
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
}
