using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Places;

namespace ParkingTracker.Shared.Features.Places.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The bar at the bottom of the map while a place is chosen in the picker (ADR-0030): the label of the place, the hint to
///              drag the pin, Cancel and Save, and Remove when the place of this kind is already saved. Save is disabled for a place
///              outside the Brussels box, with the reason shown. It colours itself with the variables of the app, so that it reads in
///              the light and the dark scheme.
/// </summary>
public sealed partial class PlaceConfirmBar : ViewModelComponent<PlacePickerViewModel>
{
	#region Constants
	/// <summary>
	/// The reason shown while the pin is outside the Brussels box.
	/// </summary>
	private const string OutsideText = "Outside the Brussels-Capital Region: move the pin.";
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles "Cancel".
	/// </summary>
	private void OnCancel() => ViewModel.Cancel();

	/// <summary>
	/// Handles "Save".
	/// </summary>
	/// <returns>A task that completes when the place was saved or the user was told why not.</returns>
	private Task OnSaveAsync() => ViewModel.SaveAsync();

	/// <summary>
	/// Handles "Remove".
	/// </summary>
	/// <returns>A task that completes when the place was removed or the user was told why not.</returns>
	private Task OnRemoveAsync() => ViewModel.RemoveAsync();
	#endregion
}
