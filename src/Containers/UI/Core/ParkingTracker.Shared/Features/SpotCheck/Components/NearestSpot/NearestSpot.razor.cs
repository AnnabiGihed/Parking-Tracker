using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.Shared.Features.SpotCheck.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The nearest-spot block of the bottom sheet: the search state, the best spot with the walk, buttons that open it in
///              Waze or Google Maps through the link launcher, and the other spots nearby.
/// </summary>
public sealed partial class NearestSpot : ViewModelComponent<NearestSpotViewModel>
{
	#region Properties
	/// <summary>
	/// Gets the CSS classes of the block, which is hidden until a search runs.
	/// </summary>
	private string BlockClass => ViewModel.State == NearestSpotState.Hidden ? "spot hidden" : "spot";
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles the Waze button.
	/// </summary>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	private Task OnWazeAsync() => ViewModel.OpenWazeAsync();

	/// <summary>
	/// Handles the Google Maps button.
	/// </summary>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	private Task OnGoogleMapsAsync() => ViewModel.OpenGoogleMapsAsync();
	#endregion
}
