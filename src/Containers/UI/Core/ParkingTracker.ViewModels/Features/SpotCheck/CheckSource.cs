namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Where the place of a spot check came from.
/// </summary>
public enum CheckSource
{
	/// <summary>
	/// The position of the user, from the location tracker.
	/// </summary>
	Me,

	/// <summary>
	/// A pin the user dropped on the map or found with the search.
	/// </summary>
	Pin,
}
