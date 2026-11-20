using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Places;

namespace ParkingTracker.Shared.Features.Places.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The list of favourite places, shown as a Radzen dialog through the <c>DialogBridge</c> (ADR-0030): each row has the
///              name and the address of a favourite and an edit button, a tap on the row shows the favourite on the map, and
///              "Add a favourite" starts the map picker. It shows a text while there is none, and disables adding with the reason
///              once the limit is reached. The back button of the phone closes it, through the bridge.
/// </summary>
public sealed partial class FavouritesDialog : ViewModelComponent<FavouritesViewModel>
{
	#region Event Handlers
	/// <summary>
	/// Handles a tap on a row: the favourite is shown on the map and the list closes.
	/// </summary>
	/// <param name="favourite">The favourite.</param>
	/// <returns>A task that completes when the place was shown.</returns>
	private Task OnShowAsync(PlaceShortcut favourite) => ViewModel.ShowAsync(favourite);

	/// <summary>
	/// Handles the edit button of a row: the list closes and the map picker opens on the favourite.
	/// </summary>
	/// <param name="favourite">The favourite.</param>
	private void OnEdit(PlaceShortcut favourite) => ViewModel.Edit(favourite);

	/// <summary>
	/// Handles "Add a favourite": the list closes and the map picker opens for a new favourite.
	/// </summary>
	private void OnAdd() => ViewModel.Add();
	#endregion
}
