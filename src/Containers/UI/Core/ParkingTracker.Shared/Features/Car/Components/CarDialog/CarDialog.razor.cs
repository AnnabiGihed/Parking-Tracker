using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Car;

namespace ParkingTracker.Shared.Features.Car.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The "My car" dialog: saves where the user parked, shows it on the map, opens walking directions in Google Maps and
///              forgets the spot. It has no parking alerts switch and no timer: both belong to a later stage.
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
	private void OnSaveHere() => ViewModel.SaveHere();

	/// <summary>
	/// Handles "Show on map".
	/// </summary>
	private void OnShow() => ViewModel.ShowOnMap();

	/// <summary>
	/// Handles "forget this spot".
	/// </summary>
	private void OnForget() => ViewModel.Forget();

	/// <summary>
	/// Handles "Walk to my car".
	/// </summary>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	private Task OnWalkAsync() => ViewModel.WalkToCarAsync();
	#endregion
}
