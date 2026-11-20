using Bunit;
using NSubstitute;
using ParkingTracker.Domain.Features.Vehicles.Errors;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Shared.Features.Vehicles.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.Vehicles;
using Pivot.Framework.Domain.Shared;
using Radzen.Blazor;

namespace ParkingTracker.Shared.Tests.Features.Vehicles.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the My vehicle dialog: it opens as a Radzen bottom sheet through the bridge with every field the plan names,
///              shows the plugs, battery and range only for an electric vehicle or a plug-in hybrid, writes the plate in capitals,
///              saves through the store with a toast and closes, shows the rule of the domain that the values break, offers "Remove" only for a
///              saved vehicle, and closes with the back button of the phone.
/// </summary>
public sealed class VehicleDialogTests : UiTestContext
{
	#region Test Infrastructure
	/// <summary>
	/// A saved electric car.
	/// </summary>
	private static readonly VehicleDetails Electric = new("tesla", "Model 3", 2022, FuelType.Electric, EuroNorm.Unknown, new ChargingDetails([ChargingConnector.Type2], 57.5, 450), "1ABC234");

	/// <summary>
	/// Renders the Radzen host and the dialog, and opens it.
	/// </summary>
	/// <param name="saved">The vehicle the store holds, or <see langword="null"/> for none.</param>
	/// <returns>The Radzen dialog host, which holds the dialog while it is open.</returns>
	private async Task<IRenderedComponent<RadzenDialog>> OpenAsync(VehicleDetails? saved = null)
	{
		Vehicles.GetAsync(Arg.Any<CancellationToken>()).Returns(saved);
		var host = Render<RadzenDialog>();
		Render<VehicleDialog>();
		await Resolve<VehicleViewModel>().LoadAsync().ConfigureAwait(false);

		await host.InvokeAsync(Resolve<VehicleViewModel>().Open).ConfigureAwait(false);

		await host.WaitForAssertionAsync(() => host.FindAll("#vehicleForm").Should().HaveCount(1)).ConfigureAwait(false);
		return host;
	}
	#endregion

	#region Field Tests
	/// <summary>
	/// Verifies that nothing is drawn while the dialog is closed, and that it opens in a Radzen bottom sheet with its title.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_ShouldShowTheSheetWithItsTitle()
	{
		var closed = Render<RadzenDialog>();
		Render<VehicleDialog>();
		closed.FindAll("#vehicleForm").Should().BeEmpty();

		await closed.InvokeAsync(Resolve<VehicleViewModel>().Open);

		await closed.WaitForAssertionAsync(() => closed.Find(".rz-dialog-title").TextContent.Should().Be("My vehicle"));
		closed.FindAll(".rz-dialog.app-sheet").Should().HaveCount(1);
		Resolve<IBackNavigation>().CanGoBack.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a new vehicle shows the fields that every vehicle has, with their labels, and no charging fields or "Remove".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Fields_NewPetrolVehicle_ShouldShowTheCommonFieldsOnly()
	{
		var host = await OpenAsync();

		foreach (var id in new[] { "#vehicleMake", "#vehicleModel", "#vehicleYear", "#vehicleFuel", "#vehicleEuro", "#vehiclePlate", "#vehicleSave" })
		{
			host.FindAll(id).Should().HaveCount(1, id);
		}

		host.FindAll("#vehicleConnectors, #vehicleBattery, #vehicleRange, #vehicleRemove").Should().BeEmpty();
		host.FindAll("label").Select(label => label.TextContent).Should().Equal("Make", "Model", "Year", "Fuel", "Emission standard", "Plate");
		host.Find("#vehiclePlate").GetAttribute("autocapitalize").Should().Be("characters");
		host.Find("#vehiclePlate").ClassList.Should().Contain("plate-input");
	}

	/// <summary>
	/// Verifies that an electric vehicle shows the plugs, battery and range, and that another fuel hides them again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Fields_FuelChanged_ShouldShowChargingFieldsOnlyForPlugIns()
	{
		var host = await OpenAsync();

		host.FindAll("#vehicleEuro").Should().HaveCount(1);
		await host.InvokeAsync(() => Resolve<VehicleViewModel>().SelectFuel(FuelType.Electric));

		await host.WaitForAssertionAsync(() =>
		{
			host.FindAll("#vehicleEuro").Should().BeEmpty();
			host.FindAll("#vehicleConnectors").Should().HaveCount(1);
			host.FindAll("#vehicleBattery").Should().HaveCount(1);
			host.FindAll("#vehicleRange").Should().HaveCount(1);
		});
		host.FindAll("#vehicleConnectors .rz-chkbox-label, #vehicleConnectors label").Select(label => label.TextContent.Trim()).Should().Contain(["Type 2", "CCS", "CHAdeMO", "Type 1"]);

		await host.InvokeAsync(() => Resolve<VehicleViewModel>().SelectFuel(FuelType.Diesel));

		await host.WaitForAssertionAsync(() => host.FindAll("#vehicleConnectors, #vehicleBattery, #vehicleRange").Should().BeEmpty());
		host.FindAll("#vehicleEuro").Should().HaveCount(1);
	}

	/// <summary>
	/// Verifies that a saved vehicle fills the fields and offers "Remove".
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Fields_VehicleSaved_ShouldBeFilledAndOfferRemove()
	{
		var host = await OpenAsync(Electric);

		host.Find("#vehicleModel").GetAttribute("value").Should().Be("Model 3");
		host.Find("#vehiclePlate").GetAttribute("value").Should().Be("1ABC234");
		host.FindAll("#vehicleConnectors").Should().HaveCount(1);
		host.FindAll("#vehicleRemove").Should().HaveCount(1);
	}

	/// <summary>
	/// Verifies that the plate is written in capitals as it is typed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Plate_Typed_ShouldBeUpperCase()
	{
		var host = await OpenAsync();

		await host.Find("#vehiclePlate").InputAsync("1abc234");

		Resolve<VehicleViewModel>().Plate.Should().Be("1ABC234");
	}
	#endregion

	#region Save Tests
	/// <summary>
	/// Verifies that a submit saves through the store, shows the toast and closes the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_ValidValues_ShouldSaveToastAndClose()
	{
		var host = await OpenAsync();
		var vehicle = Resolve<VehicleViewModel>();
		vehicle.Make = "renault";
		vehicle.Year = 2019;

		await host.Find("#vehicleSave").ClickAsync();

		await Vehicles.Received(1).SaveAsync(Arg.Is<VehicleDetails>(details => details.Make == "renault" && details.Year == 2019), Arg.Any<CancellationToken>());
		Resolve<ToastViewModel>().Message.Should().Be("Vehicle saved.");
		await host.WaitForAssertionAsync(() => host.FindAll("#vehicleForm").Should().BeEmpty());
		vehicle.IsOpen.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the rule of the domain that the values break is shown in the form and the dialog stays open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_DomainRefuses_ShouldShowTheMessage()
	{
		Vehicles.SaveAsync(Arg.Any<VehicleDetails>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(VehicleErrors.MakeUnknown));
		var host = await OpenAsync();
		Resolve<VehicleViewModel>().Year = 2019;

		await host.Find("#vehicleSave").ClickAsync();

		await host.WaitForAssertionAsync(() => host.Find("#vehicleError").TextContent.Should().Be(VehicleErrors.MakeUnknown.Message));
		host.FindAll("#vehicleForm").Should().HaveCount(1);
	}

	/// <summary>
	/// Verifies that an empty year shows "Enter the model year." in the form and saves nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_EmptyYear_ShouldAskForTheYear()
	{
		var host = await OpenAsync();

		await host.Find("#vehicleSave").ClickAsync();

		await host.WaitForAssertionAsync(() => host.Find("#vehicleError").TextContent.Should().Be("Enter the model year."));
		await Vehicles.DidNotReceive().SaveAsync(Arg.Any<VehicleDetails>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that "Remove" removes the saved vehicle, shows the toast and closes the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_Tap_ShouldRemoveToastAndClose()
	{
		var host = await OpenAsync(Electric);

		await host.Find("#vehicleRemove").ClickAsync();

		await Vehicles.Received(1).RemoveAsync(Arg.Any<CancellationToken>());
		Resolve<ToastViewModel>().Message.Should().Be("Vehicle removed.");
		await host.WaitForAssertionAsync(() => host.FindAll("#vehicleForm").Should().BeEmpty());
	}
	#endregion

	#region Back Button Tests
	/// <summary>
	/// Verifies that the back button of the phone closes the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task BackButton_ShouldCloseTheDialog()
	{
		var host = await OpenAsync();

		var closed = await host.InvokeAsync(Resolve<IBackNavigation>().TryGoBack);

		closed.Should().BeTrue();
		await host.WaitForAssertionAsync(() => host.FindAll("#vehicleForm").Should().BeEmpty());
		Resolve<VehicleViewModel>().IsOpen.Should().BeFalse();
	}
	#endregion
}
