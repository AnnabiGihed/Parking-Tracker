using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Domain.Features.Vehicles.Errors;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.Vehicles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Vehicles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="VehicleViewModel"/>: the saved vehicle fills the form, the plugs, battery and range show only for an
///              electric vehicle or a plug-in hybrid and are cleared when the fuel changes, the plate is written in capitals, a broken rule of the
///              domain is shown next to the form, a save and a removal show their toast and close the dialog, and the menu text and the changes
///              are passed on.
/// </summary>
public sealed class VehicleViewModelTests
{
	#region Test Infrastructure
	/// <summary>
	/// An electric car with plugs, a battery, a range and a plate.
	/// </summary>
	private static readonly VehicleDetails Electric = new("tesla", "Model 3", 2022, FuelType.Electric, EuroNorm.Unknown, new ChargingDetails([ChargingConnector.Type2, ChargingConnector.Ccs], 57.5, 450), "1ABC234");

	/// <summary>
	/// Builds the view model over a store that holds the given vehicle.
	/// </summary>
	/// <param name="stored">The vehicle the store holds, or <see langword="null"/> for none.</param>
	/// <returns>The view model, the store and the toast.</returns>
	private static (VehicleViewModel Vehicle, IVehicleStore Store, ToastViewModel Toast) Arrange(VehicleDetails? stored = null)
	{
		var store = Substitute.For<IVehicleStore>();
		store.GetAsync(Arg.Any<CancellationToken>()).Returns(stored);
		store.SaveAsync(Arg.Any<VehicleDetails>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
		var toast = new ToastViewModel(clock);
		return (new VehicleViewModel(store, toast, clock), store, toast);
	}

	/// <summary>
	/// Fills the fields of a valid petrol car.
	/// </summary>
	/// <param name="vehicle">The view model.</param>
	private static void FillPetrol(VehicleViewModel vehicle)
	{
		vehicle.Make = "renault";
		vehicle.Model = " Clio ";
		vehicle.Year = 2019;
		vehicle.EuroNorm = EuroNorm.Euro6;
	}
	#endregion

	#region Load Tests
	/// <summary>
	/// Verifies that nothing saved reads "Not set" and an empty form.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Load_NothingSaved_ShouldReadNotSet()
	{
		var (vehicle, _, _) = Arrange();

		await vehicle.LoadAsync();
		vehicle.Open();

		vehicle.HasVehicle.Should().BeFalse();
		vehicle.SummaryText.Should().Be("Not set");
		vehicle.Make.Should().BeEmpty();
		vehicle.Year.Should().BeNull();
		vehicle.Fuel.Should().Be(FuelType.Petrol);
		vehicle.ShowsChargingFields.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a saved vehicle fills the form on open and reads "Make Model · Year" in the menu.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_VehicleSaved_ShouldFillTheForm()
	{
		var (vehicle, _, _) = Arrange(Electric);
		await vehicle.LoadAsync();

		vehicle.Open();

		vehicle.IsOpen.Should().BeTrue();
		vehicle.HasVehicle.Should().BeTrue();
		vehicle.SummaryText.Should().Be("Tesla Model 3 · 2022");
		vehicle.Make.Should().Be("tesla");
		vehicle.Model.Should().Be("Model 3");
		vehicle.Year.Should().Be(2022);
		vehicle.Fuel.Should().Be(FuelType.Electric);
		vehicle.Connectors.Should().Equal(ChargingConnector.Type2, ChargingConnector.Ccs);
		vehicle.BatteryKwh.Should().Be(57.5);
		vehicle.RangeKm.Should().Be(450);
		vehicle.Plate.Should().Be("1ABC234");
		vehicle.ShowsChargingFields.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a vehicle without a model reads the make and the year only.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SummaryText_NoModel_ShouldReadMakeAndYear()
	{
		var (vehicle, _, _) = Arrange(new VehicleDetails("other", null, 2010, FuelType.Diesel, EuroNorm.Euro4));

		await vehicle.LoadAsync();

		vehicle.SummaryText.Should().Be("Other · 2010");
	}

	/// <summary>
	/// Verifies that an unreadable vehicle is reported to the user and reads as none.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Load_Unreadable_ShouldToastTheMessage()
	{
		var (vehicle, store, toast) = Arrange();
		store.GetAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new SavedDataUnreadableException(SavedDataUnreadableException.VehicleMessage, "x", false));

		await vehicle.LoadAsync();

		toast.Message.Should().Be(SavedDataUnreadableException.VehicleMessage);
		vehicle.HasVehicle.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a refused read is reported with the general text.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Load_DatabaseRefused_ShouldToastTheGeneralText()
	{
		var (vehicle, store, toast) = Arrange();
		store.GetAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db"));

		await vehicle.LoadAsync();

		toast.Message.Should().Be(VehicleViewModel.ReadFailedText);
	}
	#endregion

	#region Field Tests
	/// <summary>
	/// Verifies that the plate is written in capitals while it is typed.
	/// </summary>
	[Fact]
	public void SetPlate_LowerCase_ShouldBeUpperCase()
	{
		var (vehicle, _, _) = Arrange();

		vehicle.SetPlate("1abc234");

		vehicle.Plate.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that the plugs, battery and range show only for an electric vehicle and a plug-in hybrid.
	/// </summary>
	/// <param name="fuel">The fuel.</param>
	/// <param name="shown">Whether the charging fields show.</param>
	[Theory]
	[InlineData(FuelType.Petrol, false)]
	[InlineData(FuelType.Diesel, false)]
	[InlineData(FuelType.Hybrid, false)]
	[InlineData(FuelType.Gas, false)]
	[InlineData(FuelType.PlugInHybrid, true)]
	[InlineData(FuelType.Electric, true)]
	public void SelectFuel_ShouldShowChargingFieldsOnlyForPlugIns(FuelType fuel, bool shown)
	{
		var (vehicle, _, _) = Arrange();

		vehicle.SelectFuel(fuel);

		vehicle.ShowsChargingFields.Should().Be(shown);
	}

	/// <summary>
	/// Verifies that switching from an electric vehicle to another fuel clears the plugs, battery and range, and keeps them for a plug-in hybrid.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SelectFuel_AwayFromPlugIn_ShouldClearTheChargingFields()
	{
		var (vehicle, _, _) = Arrange(Electric);
		await vehicle.LoadAsync();
		vehicle.Open();

		vehicle.SelectFuel(FuelType.PlugInHybrid);
		vehicle.Connectors.Should().HaveCount(2);
		vehicle.SelectFuel(FuelType.Diesel);

		vehicle.Connectors.Should().BeEmpty();
		vehicle.BatteryKwh.Should().BeNull();
		vehicle.RangeKm.Should().BeNull();
	}

	/// <summary>
	/// Verifies that the year bounds come from the domain and the clock.
	/// </summary>
	[Fact]
	public void YearBounds_ShouldBeFromTheDomainToNextYear()
	{
		var (vehicle, _, _) = Arrange();

		VehicleViewModel.MinimumYear.Should().Be(1950);
		vehicle.MaximumYear.Should().Be(2027);
	}

	/// <summary>
	/// Verifies that the lists of choices have the readable texts of the dialog.
	/// </summary>
	[Fact]
	public void Options_ShouldHaveReadableTexts()
	{
		VehicleOptions.Fuels.Select(option => option.Text).Should().Equal("Petrol", "Diesel", "Hybrid", "Plug-in hybrid", "Electric", "LPG / CNG");
		VehicleOptions.EuroNorms.Select(option => option.Text).Should().Equal("Euro 1", "Euro 2", "Euro 3", "Euro 4", "Euro 5", "Euro 6", "Unknown");
		VehicleOptions.Connectors.Select(option => option.Text).Should().Equal("Type 2", "CCS", "CHAdeMO", "Type 1");
		VehicleOptions.Makes[^1].Should().Be(new VehicleOption<string>("other", "Other"));
	}
	#endregion

	#region Save Tests
	/// <summary>
	/// Verifies that a save maps the fields to the values of the domain, shows the toast and closes the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_ValidPetrolCar_ShouldSaveToastAndClose()
	{
		var (vehicle, store, toast) = Arrange();
		vehicle.Open();
		FillPetrol(vehicle);
		vehicle.SetPlate("1abc234");

		await vehicle.SaveAsync();

		await store.Received(1).SaveAsync(Arg.Is<VehicleDetails>(details => details.Make == "renault" && details.Model == "Clio" && details.Year == 2019 && details.Fuel == FuelType.Petrol && details.EuroNorm == EuroNorm.Euro6 && details.Charging == null && details.Plate == "1ABC234"), Arg.Any<CancellationToken>());
		toast.Message.Should().Be("Vehicle saved.");
		vehicle.IsOpen.Should().BeFalse();
		vehicle.FormError.Should().BeEmpty();
		vehicle.HasVehicle.Should().BeTrue();
		vehicle.SummaryText.Should().Be("Renault Clio · 2019");
	}

	/// <summary>
	/// Verifies that an electric vehicle is saved with its charging details.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_ElectricCar_ShouldSendTheChargingDetails()
	{
		var (vehicle, store, _) = Arrange();
		vehicle.Open();
		FillPetrol(vehicle);
		vehicle.SelectFuel(FuelType.Electric);
		vehicle.SetConnectors([ChargingConnector.Ccs, ChargingConnector.Type2]);
		vehicle.BatteryKwh = 60;
		vehicle.RangeKm = 400;

		await vehicle.SaveAsync();

		await store.Received(1).SaveAsync(Arg.Is<VehicleDetails>(details => details.Charging != null && Math.Abs(details.Charging.BatteryKwh!.Value - 60) < 0.001 && details.Charging.RangeKm == 400 && details.Charging.Connectors.SequenceEqual(new[] { ChargingConnector.Type2, ChargingConnector.Ccs })), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a rule of the domain that the values break is shown next to the form, with no toast, and the dialog stays open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_DomainRefuses_ShouldShowItsMessageAndStayOpen()
	{
		var (vehicle, store, toast) = Arrange();
		store.SaveAsync(Arg.Any<VehicleDetails>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(VehicleErrors.MakeUnknown));
		vehicle.Open();
		vehicle.Year = 2019;

		await vehicle.SaveAsync();

		vehicle.FormError.Should().Be(VehicleErrors.MakeUnknown.Message);
		vehicle.IsOpen.Should().BeTrue();
		toast.IsVisible.Should().BeFalse();
		vehicle.HasVehicle.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that an empty year shows "Enter the model year." and writes nothing, and that the dialog stays open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_EmptyYear_ShouldAskForTheYear()
	{
		var (vehicle, store, _) = Arrange();
		vehicle.Open();
		FillPetrol(vehicle);
		vehicle.Year = null;

		await vehicle.SaveAsync();

		vehicle.FormError.Should().Be("Enter the model year.");
		await store.DidNotReceive().SaveAsync(Arg.Any<VehicleDetails>(), Arg.Any<CancellationToken>());
		vehicle.IsOpen.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a year outside the range goes to the domain, which answers with its range message.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_YearOutOfRange_ShouldShowTheMessageOfTheDomain()
	{
		var (vehicle, store, _) = Arrange();
		store.SaveAsync(Arg.Any<VehicleDetails>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(VehicleErrors.YearOutOfRange(2027)));
		vehicle.Open();
		FillPetrol(vehicle);
		vehicle.Year = 1900;

		await vehicle.SaveAsync();

		vehicle.FormError.Should().Be(VehicleErrors.YearOutOfRange(2027).Message);
	}

	/// <summary>
	/// Verifies that an electric vehicle has no emission standard: it is hidden, reset to unknown on the switch, and saved as unknown.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SelectFuel_Electric_ShouldResetAndHideTheEuroNorm()
	{
		var (vehicle, store, _) = Arrange();
		vehicle.Open();
		FillPetrol(vehicle);
		vehicle.ShowsEuroNorm.Should().BeTrue();

		vehicle.SelectFuel(FuelType.Electric);
		await vehicle.SaveAsync();

		vehicle.ShowsEuroNorm.Should().BeFalse();
		vehicle.EuroNorm.Should().Be(EuroNorm.Unknown);
		await store.Received(1).SaveAsync(Arg.Is<VehicleDetails>(details => details.EuroNorm == EuroNorm.Unknown), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a plug-in hybrid keeps its emission standard.
	/// </summary>
	[Fact]
	public void SelectFuel_PlugInHybrid_ShouldKeepTheEuroNorm()
	{
		var (vehicle, _, _) = Arrange();
		vehicle.EuroNorm = EuroNorm.Euro6;

		vehicle.SelectFuel(FuelType.PlugInHybrid);

		vehicle.ShowsEuroNorm.Should().BeTrue();
		vehicle.EuroNorm.Should().Be(EuroNorm.Euro6);
	}

	/// <summary>
	/// Verifies that a refused write is shown as a general text and the dialog stays open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_DatabaseRefuses_ShouldShowTheGeneralText()
	{
		var (vehicle, store, toast) = Arrange();
		store.SaveAsync(Arg.Any<VehicleDetails>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db"));
		vehicle.Open();
		vehicle.Year = 2019;

		await vehicle.SaveAsync();

		vehicle.FormError.Should().Be(VehicleViewModel.SaveFailedText);
		vehicle.IsOpen.Should().BeTrue();
		toast.IsVisible.Should().BeFalse();
		vehicle.IsBusy.Should().BeFalse();
	}
	#endregion

	#region Remove Tests
	/// <summary>
	/// Verifies that a removal removes the vehicle, shows the toast, closes the dialog and the menu reads "Not set".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_VehicleSaved_ShouldRemoveToastAndClose()
	{
		var (vehicle, store, toast) = Arrange(Electric);
		await vehicle.LoadAsync();
		vehicle.Open();

		await vehicle.RemoveAsync();

		await store.Received(1).RemoveAsync(Arg.Any<CancellationToken>());
		toast.Message.Should().Be("Vehicle removed.");
		vehicle.IsOpen.Should().BeFalse();
		vehicle.HasVehicle.Should().BeFalse();
		vehicle.SummaryText.Should().Be("Not set");
	}

	/// <summary>
	/// Verifies that nothing is removed while no vehicle is saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_NothingSaved_ShouldDoNothing()
	{
		var (vehicle, store, toast) = Arrange();
		vehicle.Open();

		await vehicle.RemoveAsync();

		await store.DidNotReceive().RemoveAsync(Arg.Any<CancellationToken>());
		toast.IsVisible.Should().BeFalse();
		vehicle.IsOpen.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a refused removal is shown as a general text and the vehicle stays.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_DatabaseRefuses_ShouldShowTheGeneralText()
	{
		var (vehicle, store, _) = Arrange(Electric);
		store.RemoveAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db"));
		await vehicle.LoadAsync();
		vehicle.Open();

		await vehicle.RemoveAsync();

		vehicle.FormError.Should().Be(VehicleViewModel.RemoveFailedText);
		vehicle.HasVehicle.Should().BeTrue();
	}
	#endregion

	#region Event Tests
	/// <summary>
	/// Verifies that opening, closing and saving raise Changed, so that the menu and the dialog render again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Changed_OpenCloseAndSave_ShouldBeRaised()
	{
		var (vehicle, _, _) = Arrange();
		var raised = 0;
		vehicle.Changed += (_, _) => raised++;

		vehicle.Open();
		var afterOpen = raised;
		vehicle.Close();
		var afterClose = raised;
		vehicle.Open();
		FillPetrol(vehicle);
		await vehicle.SaveAsync();

		afterOpen.Should().Be(1);
		afterClose.Should().Be(2);
		raised.Should().BeGreaterThan(afterClose);
	}

	/// <summary>
	/// Verifies that opening twice and closing a closed dialog do nothing.
	/// </summary>
	[Fact]
	public void OpenAndClose_Repeated_ShouldDoNothingTheSecondTime()
	{
		var (vehicle, _, _) = Arrange();
		var raised = 0;
		vehicle.Changed += (_, _) => raised++;

		vehicle.Close();
		vehicle.Open();
		vehicle.Open();

		raised.Should().Be(1);
	}
	#endregion

	#region Argument Tests
	/// <summary>
	/// Verifies that the constructor rejects a missing argument.
	/// </summary>
	[Fact]
	public void Constructor_NullArguments_ShouldThrowArgumentNullException()
	{
		var store = Substitute.For<IVehicleStore>();
		var clock = new FakeTimeProvider();
		var toast = new ToastViewModel(clock);

		var noStore = () => new VehicleViewModel(null!, toast, clock); // null on purpose: proves the guard clause rejects it
		var noToast = () => new VehicleViewModel(store, null!, clock); // null on purpose: proves the guard clause rejects it
		var noClock = () => new VehicleViewModel(store, toast, null!); // null on purpose: proves the guard clause rejects it

		noStore.Should().Throw<ArgumentNullException>();
		noToast.Should().Throw<ArgumentNullException>();
		noClock.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
