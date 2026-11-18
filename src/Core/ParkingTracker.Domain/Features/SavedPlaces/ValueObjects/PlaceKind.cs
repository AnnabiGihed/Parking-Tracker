namespace ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The kinds of place a user can save.
/// </summary>
public enum PlaceKind
{
	/// <summary>
	/// The user's home; there is at most one.
	/// </summary>
	Home,

	/// <summary>
	/// The user's work place; there is at most one.
	/// </summary>
	Work,

	/// <summary>
	/// Any other place the user likes to return to; up to 20.
	/// </summary>
	Favourite,
}
