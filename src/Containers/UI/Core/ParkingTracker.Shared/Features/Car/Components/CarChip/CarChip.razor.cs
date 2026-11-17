using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Car;

namespace ParkingTracker.Shared.Features.Car.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The "My car" chip under the search bar, shown while a parked car is saved or a timer runs. With a timer it shows the
///              time left and turns late 10 minutes before the end. It opens the car dialog.
/// </summary>
public sealed partial class CarChip : ViewModelComponent<CarViewModel>
{
	#region Event Handlers
	/// <summary>
	/// Handles a tap on the chip.
	/// </summary>
	private void OnClick() => ViewModel.Open();
	#endregion
}
