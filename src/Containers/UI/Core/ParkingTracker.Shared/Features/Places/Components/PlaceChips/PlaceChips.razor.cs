using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.ViewModels.Features.Places;

namespace ParkingTracker.Shared.Features.Places.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The Home and Work chips under the search bar: a tap on a saved place shows it on the map, a tap on a place that is not
///              set opens the place dialog, and a small pencil on a saved place opens the dialog to change or remove it.
/// </summary>
public sealed partial class PlaceChips : ViewModelComponent<PlacesViewModel>
{
	#region Lifecycle Methods
	/// <inheritdoc />
	protected override async Task OnInitializedAsync()
	{
		await ViewModel.LoadAsync();
	}
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles a tap on the Home chip.
	/// </summary>
	/// <returns>A task that completes when the place was shown.</returns>
	private Task OnGoHomeAsync() => ViewModel.GoAsync(PlaceKind.Home);

	/// <summary>
	/// Handles a tap on the Work chip.
	/// </summary>
	/// <returns>A task that completes when the place was shown.</returns>
	private Task OnGoWorkAsync() => ViewModel.GoAsync(PlaceKind.Work);

	/// <summary>
	/// Handles the pencil of the Home chip.
	/// </summary>
	private void OnEditHome() => ViewModel.Open(PlaceKind.Home);

	/// <summary>
	/// Handles the pencil of the Work chip.
	/// </summary>
	private void OnEditWork() => ViewModel.Open(PlaceKind.Work);
	#endregion
}
