using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Places;

namespace ParkingTracker.Shared.Features.Places.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The dialog that sets, changes or removes the Home or Work place: the user searches an address, uses the position on
///              screen, or removes a saved place.
/// </summary>
public sealed partial class PlaceDialog : ViewModelComponent<PlacesViewModel>
{
	#region Properties
	/// <summary>
	/// Gets the title of the dialog: the name of the kind of place being edited.
	/// </summary>
	private string Title => ViewModel.EditingKind.ToString();
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles the close button.
	/// </summary>
	private void OnClose() => ViewModel.Close();

	/// <summary>
	/// Handles "Search".
	/// </summary>
	/// <returns>A task that completes when the place was saved or the user was told why not.</returns>
	private Task OnSearchAsync() => ViewModel.SaveFromAddressAsync();

	/// <summary>
	/// Handles "Use my position".
	/// </summary>
	/// <returns>A task that completes when the place was saved or the user was told why not.</returns>
	private Task OnUseMyPositionAsync() => ViewModel.SaveFromMyPositionAsync();

	/// <summary>
	/// Handles "Remove".
	/// </summary>
	/// <returns>A task that completes when the place was removed.</returns>
	private Task OnRemoveAsync() => ViewModel.RemoveAsync();
	#endregion
}
