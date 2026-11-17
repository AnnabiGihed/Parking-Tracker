using NSubstitute;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Car;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CarViewModel"/>: the car is saved where the user or
///              the pin is, with the headline the app showed; without a position nothing is saved; walking directions, "show on
///              map" and "forget" work from the saved car.
/// </summary>
public sealed class CarViewModelTests
{
	#region Save Tests
	/// <summary>
	/// Verifies that without a position nothing is saved and the user is told.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveHere_NoPosition_ShouldTellTheUserAndSaveNothing()
	{
		using var ui = new UiFixture();

		var saved = await ui.Car.SaveHereAsync();

		saved.Should().BeFalse();
		ui.Toast.Message.Should().Be("No position yet: turn on my location or tap the map");
		_ = ui.Settings.DidNotReceiveWithAnyArgs().SaveCarAsync(default!);
	}

	/// <summary>
	/// Verifies that the car is saved at the pin with the headline of the last answer and the moment of the clock.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SaveHere_WithPin_ShouldSaveTheCarWithTheHeadline()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.HandleMapClickAsync(UiFixture.Here);

		var saved = await ui.Car.SaveHereAsync();

		saved.Should().BeTrue();
		_ = ui.Settings.Received(1).SaveCarAsync(new ParkedCar(UiFixture.Here, UiFixture.Start, "Paid parking", string.Empty));
		ui.Car.Car.Should().NotBeNull();
		ui.Toast.Message.Should().Be("Car position saved");
		ui.Car.Info.Should().Be("Parked 17/11/2026 10:00");
		ui.Car.AppSaid.Should().Be("Paid parking");
	}

	/// <summary>
	/// Verifies that "I parked here" saves the car and opens the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ParkHere_WithPin_ShouldSaveAndOpenTheDialog()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Free, "x"));
		await ui.Spot.HandleMapClickAsync(UiFixture.Here);

		await ui.Car.ParkHereAsync();

		ui.Car.IsOpen.Should().BeTrue();
		ui.Car.Car.Should().NotBeNull();
	}
	#endregion

	#region Saved Car Tests
	/// <summary>
	/// Verifies that the saved car is read when loaded and walking directions to it are opened in Google Maps.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task WalkToCar_SavedCar_ShouldOpenWalkingDirections()
	{
		using var ui = SavedCarFixture();
		var car = await BuildCarViewModelAsync(ui);
		car.Open();

		await car.WalkToCarAsync();

		await ui.Launcher.Received(1).OpenAsync(new Uri("https://www.google.com/maps/dir/?api=1&destination=50.825000,4.360600&travelmode=walking"), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that "show on map" closes the dialog and asks the map to centre on the car at zoom level 18.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ShowOnMap_SavedCar_ShouldAskTheMapToCentreOnTheCar()
	{
		using var ui = SavedCarFixture();
		var car = await BuildCarViewModelAsync(ui);
		MapViewRequest? shown = null;
		car.ShowRequested += (_, e) => shown = e.Value;
		car.Open();

		car.ShowOnMap();

		shown.Should().Be(new MapViewRequest(UiFixture.Here, 18));
		car.IsOpen.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that forgetting the car clears the setting, the car and its popup.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Forget_SavedCar_ShouldClearTheSettingAndTheCar()
	{
		using var ui = SavedCarFixture();
		var car = await BuildCarViewModelAsync(ui);
		car.Open();

		await car.ForgetAsync();

		_ = ui.Settings.Received(1).ClearCarAsync();
		car.Car.Should().BeNull();
		car.PopupHtml.Should().BeEmpty();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the fixture whose settings already hold a parked car, before any view model reads them.
	/// </summary>
	/// <returns>The fixture.</returns>
	private static UiFixture SavedCarFixture()
	{
		return new UiFixture(settings => settings.GetCarAsync().Returns(new ParkedCar(UiFixture.Here, UiFixture.Start, "Free for you here", string.Empty)));
	}

	/// <summary>
	/// Builds a car view model over the fixture and lets it read the saved car.
	/// </summary>
	/// <param name="ui">The fixture.</param>
	/// <returns>A task with the car view model, after it read the saved car.</returns>
	private static async Task<CarViewModel> BuildCarViewModelAsync(UiFixture ui)
	{
		var car = new CarViewModel(ui.Settings, ui.Launcher, ui.Spot, ui.Toast, ui.Clock);
		await car.LoadAsync().ConfigureAwait(false);
		return car;
	}
	#endregion
}
