using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Features.SpotCheck.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The banner in the bottom sheet that asks the user to turn location on, or to allow it all the time, driven by the
///              <see cref="LocationPermissionViewModel"/>.
/// </summary>
public sealed partial class PermissionBanner : ViewModelComponent<LocationPermissionViewModel>
{
	#region Event Handlers
	/// <summary>
	/// Handles "Turn on".
	/// </summary>
	/// <returns>A task that completes when the request was handled.</returns>
	private Task OnTurnOnAsync() => ViewModel.TurnOnAsync();

	/// <summary>
	/// Handles "Allow".
	/// </summary>
	/// <returns>A task that completes when the request was handled.</returns>
	private Task OnAllowAsync() => ViewModel.AllowAlwaysAsync();

	/// <summary>
	/// Handles "Later".
	/// </summary>
	/// <returns>A task that completes when the banner was hidden.</returns>
	private Task OnLaterAsync() => ViewModel.PostponeAsync();
	#endregion
}
