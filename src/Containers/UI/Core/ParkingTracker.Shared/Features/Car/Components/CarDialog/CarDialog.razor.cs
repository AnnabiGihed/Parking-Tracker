using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Car;

namespace ParkingTracker.Shared.Features.Car.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The "My car" dialog: saves where the user parked, shows it on the map, opens walking directions in Google Maps and
///              forgets the spot. It also has the parking alerts switch and the parking timer (presets, minutes, start and stop).
/// </summary>
public sealed partial class CarDialog : ViewModelComponent<CarViewModel>
{
	#region Event Handlers
	/// <summary>
	/// Handles the close button.
	/// </summary>
	private void OnClose() => ViewModel.Close();

	/// <summary>
	/// Handles "My car is parked here" and "Car is here now".
	/// </summary>
	/// <returns>A task that completes when the car was saved or the missing position was reported.</returns>
	private async Task OnSaveHereAsync()
	{
		await ViewModel.SaveHereAsync();
	}

	/// <summary>
	/// Handles "Show on map".
	/// </summary>
	private void OnShow() => ViewModel.ShowOnMap();

	/// <summary>
	/// Handles "forget this spot".
	/// </summary>
	/// <returns>A task that completes when the car was forgotten.</returns>
	private Task OnForgetAsync() => ViewModel.ForgetAsync();

	/// <summary>
	/// Handles the parking alerts switch.
	/// </summary>
	/// <param name="enabled">The new state of the switch.</param>
	/// <returns>A task that completes when the switch is saved and the service follows it.</returns>
	private Task OnAlertsChangedAsync(bool enabled) => ViewModel.SetAlertsAsync(enabled);

	/// <summary>
	/// Handles a preset button of the timer.
	/// </summary>
	/// <param name="minutes">The length of the preset.</param>
	private void OnPreset(int minutes) => ViewModel.SetMinutes(minutes);

	/// <summary>
	/// Handles an edit of the minutes field; an empty field counts as zero, which the timer raises to its minimum.
	/// </summary>
	/// <param name="minutes">The new value of the field, or <see langword="null"/> when it was emptied.</param>
	private void OnMinutesChanged(int? minutes) => ViewModel.SetMinutes(minutes ?? 0);

	/// <summary>
	/// Handles "Start".
	/// </summary>
	/// <returns>A task that completes when the timer started or was refused.</returns>
	private Task<bool> OnStartAsync() => ViewModel.StartTimerAsync();

	/// <summary>
	/// Handles "Stop the timer".
	/// </summary>
	/// <returns>A task that completes when the timer is stopped.</returns>
	private Task OnStopAsync() => ViewModel.StopTimerAsync();

	/// <summary>
	/// Handles "Walk to my car".
	/// </summary>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	private Task OnWalkAsync() => ViewModel.WalkToCarAsync();
	#endregion
}
