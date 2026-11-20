using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Vehicles;

namespace ParkingTracker.Shared.Features.Vehicles.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The My vehicle dialog (ADR-0031), shown as a bottom sheet through the <c>DialogBridge</c>: make, model, year, fuel,
///              emission standard and plate and, only while the fuel is electric or a plug-in hybrid, the plugs, the battery and the
///              range. Every label sits above its input. It hands each change to the <see cref="VehicleViewModel"/>, which holds the values,
///              checks them through the domain and shows the first broken rule next to the form. The back button of the phone closes it,
///              through the bridge.
/// </summary>
public sealed partial class VehicleDialog : ViewModelComponent<VehicleViewModel>
{
	#region Event Handlers
	/// <summary>
	/// Handles a new make in the drop-down.
	/// </summary>
	/// <param name="code">The code of the make.</param>
	private void OnMakeChanged(string? code) => ViewModel.Make = code ?? string.Empty;

	/// <summary>
	/// Handles a new model year.
	/// </summary>
	/// <param name="year">The year, or <see langword="null"/> when the field was emptied.</param>
	private void OnYearChanged(int? year) => ViewModel.Year = year;

	/// <summary>
	/// Handles a new fuel in the drop-down.
	/// </summary>
	/// <param name="fuel">The fuel.</param>
	private void OnFuelChanged(FuelType fuel) => ViewModel.SelectFuel(fuel);

	/// <summary>
	/// Handles a new emission standard in the drop-down.
	/// </summary>
	/// <param name="euroNorm">The emission standard.</param>
	private void OnEuroNormChanged(EuroNorm euroNorm) => ViewModel.EuroNorm = euroNorm;

	/// <summary>
	/// Handles a change of the ticked plugs.
	/// </summary>
	/// <param name="chosen">The plugs that are ticked now.</param>
	private void OnConnectorsChanged(IEnumerable<ChargingConnector>? chosen) => ViewModel.SetConnectors(chosen);

	/// <summary>
	/// Handles a new battery size.
	/// </summary>
	/// <param name="kilowattHours">The battery in kWh, or <see langword="null"/> when the field was emptied.</param>
	private void OnBatteryChanged(double? kilowattHours) => ViewModel.BatteryKwh = kilowattHours;

	/// <summary>
	/// Handles a new range.
	/// </summary>
	/// <param name="kilometres">The range in kilometres, or <see langword="null"/> when the field was emptied.</param>
	private void OnRangeChanged(int? kilometres) => ViewModel.RangeKm = kilometres;

	/// <summary>
	/// Handles each keystroke in the plate field and keeps the plate in capitals.
	/// </summary>
	/// <param name="typed">The text in the field.</param>
	private void OnPlateChanged(string? typed) => ViewModel.SetPlate(typed);

	/// <summary>
	/// Handles "Save vehicle".
	/// </summary>
	/// <param name="vehicle">The view model the form is bound to; not used.</param>
	/// <returns>A task that completes when the vehicle was saved or the reason it was not was shown.</returns>
	private Task OnSaveAsync(VehicleViewModel vehicle) => ViewModel.SaveAsync();

	/// <summary>
	/// Handles "Remove".
	/// </summary>
	/// <returns>A task that completes when the vehicle was removed.</returns>
	private Task OnRemoveAsync() => ViewModel.RemoveAsync();
	#endregion
}
