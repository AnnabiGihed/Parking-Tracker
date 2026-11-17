namespace ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What a street surface means for the user's card; drives the colour of the surface on the map.
/// </summary>
public enum SurfaceKind
{
	/// <summary>
	/// No parking rules.
	/// </summary>
	Free,

	/// <summary>
	/// A red zone: paid, residents' cards are never valid.
	/// </summary>
	Red,

	/// <summary>
	/// Paid parking; the card does not cover it.
	/// </summary>
	Pay,

	/// <summary>
	/// The card covers the whole surface.
	/// </summary>
	Covered,

	/// <summary>
	/// A blue zone the card does not cover: a parking disc is needed.
	/// </summary>
	Disc,

	/// <summary>
	/// The card covers some house numbers of the surface but not others.
	/// </summary>
	Mixed,

	/// <summary>
	/// It cannot be told automatically: the user has to check.
	/// </summary>
	Check,

	/// <summary>
	/// The rules are unknown, an event zone, or the official lists could not be confirmed.
	/// </summary>
	Unknown,
}
