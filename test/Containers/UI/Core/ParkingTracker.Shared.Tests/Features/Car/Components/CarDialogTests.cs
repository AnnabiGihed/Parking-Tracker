using Bunit;
using NSubstitute;
using Radzen.Blazor;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Shared.Features.Car.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Tests.Features.Car.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the car dialog, built from Radzen components: the buttons of a saved car and of no car, the
///              parking alerts switch (on by default, saves a change), the timer presets, the minutes, Start and Stop, which
///              appears only while a timer runs.
/// </summary>
public sealed class CarDialogTests : UiTestContext
{
	#region Car Tests
	/// <summary>
	/// Verifies that without a saved car the dialog offers only "My car is parked here", as a Radzen button.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_NoCar_ShouldOfferOnlyToSaveTheSpot()
	{
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(() => Resolve<CarViewModel>().Open());

		cut.Find("#carHere").TextContent.Should().Contain("My car is parked here");
		cut.FindAll("#carShow").Should().BeEmpty();
		cut.FindAll("#carClear").Should().BeEmpty();
		cut.Find("#carInfo").TextContent.Should().Contain("Save where you park");
		cut.FindComponents<RadzenButton>().Should().NotBeEmpty();
	}

	/// <summary>
	/// Verifies that a position that is not known yet is reported and nothing is saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveHere_NoPosition_ShouldSaySoAndSaveNothing()
	{
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(() => Resolve<CarViewModel>().Open());

		await cut.Find("#carHere").ClickAsync();

		Resolve<ToastViewModel>().Message.Should().Be("No position yet: turn on my location or tap the map");
		_ = Settings.DidNotReceiveWithAnyArgs().SaveCarAsync(default!);
	}

	/// <summary>
	/// Verifies that a saved car shows when and what the app said, with the walk, show and forget buttons.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_SavedCar_ShouldShowItsStateAndButtons()
	{
		Settings.GetCarAsync().Returns(new ParkedCar(Here, Start, "Free here", string.Empty));
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(LoadAndOpenAsync);

		cut.Find("#carInfo").TextContent.Should().Contain("Parked").And.Contain("The app said: Free here");
		cut.Find("#carShow").TextContent.Should().Contain("Show on map");
		cut.Find("#carHere").TextContent.Should().Contain("Car is here now");
		cut.Find("#carClear").TextContent.Should().Contain("forget this spot");
		cut.Markup.Should().Contain("Walk to my car");
	}

	/// <summary>
	/// Verifies that "Walk to my car" hands the walking link to the launcher.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Walk_SavedCar_ShouldOpenTheWalkingLink()
	{
		Settings.GetCarAsync().Returns(new ParkedCar(Here, Start, string.Empty, string.Empty));
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(LoadAndOpenAsync);

		await cut.FindComponents<RadzenButton>().First(button => button.Markup.Contains("Walk to my car", StringComparison.Ordinal)).Find("button").ClickAsync();

		_ = Launcher.Received(1).OpenAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that "forget this spot" clears the saved car, and that the dialog then closes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Forget_SavedCar_ShouldClearTheCarAndClose()
	{
		Settings.GetCarAsync().Returns(new ParkedCar(Here, Start, string.Empty, string.Empty));
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(LoadAndOpenAsync);

		await cut.Find("#carClear").ClickAsync();

		_ = Settings.Received(1).ClearCarAsync();
		cut.FindAll("#carShow").Should().BeEmpty();

		await cut.InvokeAsync(() => Resolve<CarViewModel>().Close());
		cut.FindAll("#carForm").Should().BeEmpty();
	}
	#endregion

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

		cut.FindComponent<RadzenSwitch>().Instance.Value.Should().BeTrue();

		await cut.Find("#alertsToggle").ClickAsync();

		cut.FindComponent<RadzenSwitch>().Instance.Value.Should().BeFalse();
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
		cut.Find("[data-min='120']").ClassList.Should().NotContain("on");
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

		await cut.InvokeAsync(() => cut.FindComponent<RadzenNumeric<int?>>().Instance.ValueChanged.InvokeAsync(45));
		await cut.Find("#timerStart").ClickAsync();

		_ = Settings.Received(1).SaveTimerAsync(new ParkingTimer(Start.AddMinutes(45), 45));
	}

	/// <summary>
	/// Verifies that an emptied minutes field is raised to the minimum of the timer.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_EmptyMinutes_ShouldUseTheMinimum()
	{
		var cut = Render<CarDialog>();
		await cut.InvokeAsync(() => Resolve<CarViewModel>().Open());

		await cut.InvokeAsync(() => cut.FindComponent<RadzenNumeric<int?>>().Instance.ValueChanged.InvokeAsync(null));
		await cut.Find("#timerStart").ClickAsync();

		_ = Settings.Received(1).SaveTimerAsync(new ParkingTimer(Start.AddMinutes(5), 5));
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

	#region Private Helpers
	/// <summary>
	/// Reads the saved car into the view model and opens the dialog.
	/// </summary>
	/// <returns>A task that completes when the dialog is open.</returns>
	private async Task LoadAndOpenAsync()
	{
		await Resolve<CarViewModel>().LoadAsync().ConfigureAwait(false);
		Resolve<CarViewModel>().Open();
	}
	#endregion
}
