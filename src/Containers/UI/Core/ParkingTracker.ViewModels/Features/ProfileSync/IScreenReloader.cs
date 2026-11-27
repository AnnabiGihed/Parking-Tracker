namespace ParkingTracker.ViewModels.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Makes the screens read the device database again after the sync wrote data that came from the account (ADR-0032),
///              so that the chips, the favourites, the vehicle and the card line show it without a restart.
/// </summary>
public interface IScreenReloader
{
	#region Methods
	/// <summary>
	/// Tells the stores to forget what they remembered and the view models to read their data again.
	/// </summary>
	/// <returns>A task that completes when the screens hold the new data.</returns>
	Task ReloadAsync();
	#endregion
}
