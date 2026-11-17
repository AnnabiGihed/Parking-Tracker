using Bunit;
using NSubstitute;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Shared.Features.Car.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Car;

namespace ParkingTracker.Shared.Tests.Features.Car.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the timer line of the sheet: it is drawn only while a timer runs and shows the time left, the
///              end and "time is over".
/// </summary>
public sealed class TimerLineTests : UiTestContext
{
	#region Timer Line Tests
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
	/// Verifies that the timer line says the time is over once the end has passed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task TimerLine_TimerEnded_ShouldSayTheTimeIsOver()
	{
		Settings.GetTimerAsync().Returns(new ParkingTimer(Start.AddMinutes(30), 30));
		var line = Render<TimerLine>();

		Clock.Advance(TimeSpan.FromMinutes(31));
		await line.InvokeAsync(() => Resolve<CarViewModel>().LoadAsync());

		line.Find("#timerLine").TextContent.Should().Contain(ParkingTimer.EndText);
	}
	#endregion
}
