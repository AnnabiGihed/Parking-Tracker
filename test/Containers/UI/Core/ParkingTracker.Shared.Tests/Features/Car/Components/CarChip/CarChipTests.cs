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
/// Purpose     : Component tests for the car chip: it shows the time left and turns late near the end, says "My car" without a timer,
///              and says the time is over.
/// </summary>
public sealed class CarChipTests : UiTestContext
{
	#region Car Chip Tests
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

	/// <summary>
	/// Verifies that the chip of a saved car without a timer is shown, says "My car" and is not late.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CarChip_CarSavedWithoutTimer_ShouldShowMyCarAndNotBeLate()
	{
		Settings.GetCarAsync().Returns(new ParkedCar(Here, Start, "PAY", string.Empty));
		var chip = Render<CarChip>();

		await chip.InvokeAsync(() => Resolve<CarViewModel>().LoadAsync());

		chip.Find("#carChip").ClassList.Should().NotContain("hidden").And.NotContain("late");
		chip.Find("#carChip").TextContent.Should().Contain("My car");
	}

	/// <summary>
	/// Verifies that the chip says the time is over once the end of the timer has passed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CarChip_TimerEnded_ShouldShowTimeIsOverAndBeLate()
	{
		Settings.GetTimerAsync().Returns(new ParkingTimer(Start.AddMinutes(30), 30));
		var chip = Render<CarChip>();

		Clock.Advance(TimeSpan.FromMinutes(31));
		await chip.InvokeAsync(() => Resolve<CarViewModel>().LoadAsync());

		chip.Find("#carChip").TextContent.Should().Contain("Time is over");
		chip.Find("#carChip").ClassList.Should().Contain("late");
	}
	#endregion
}
