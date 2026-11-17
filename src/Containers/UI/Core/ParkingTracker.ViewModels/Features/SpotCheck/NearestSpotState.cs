namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the nearest-spot block of the bottom sheet shows.
/// </summary>
public enum NearestSpotState
{
	/// <summary>
	/// The block is hidden.
	/// </summary>
	Hidden,

	/// <summary>
	/// The search is running.
	/// </summary>
	Loading,

	/// <summary>
	/// A spot the card covers was found.
	/// </summary>
	Found,

	/// <summary>
	/// No spot the card covers is within reach.
	/// </summary>
	NoneFound,

	/// <summary>
	/// The search could not run, for example for lack of connection.
	/// </summary>
	Failed,
}
