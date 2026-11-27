namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The five sections of the profile that follow the account (ADR-0032). The favourites list is one section: the phone
///              and the server merge it as a whole, never place by place.
/// </summary>
public enum SyncSection
{
	/// <summary>
	/// The home place.
	/// </summary>
	Home = 0,

	/// <summary>
	/// The work place.
	/// </summary>
	Work = 1,

	/// <summary>
	/// The list of favourite places.
	/// </summary>
	Favourites = 2,

	/// <summary>
	/// The vehicle profile.
	/// </summary>
	Vehicle = 3,

	/// <summary>
	/// The residents' card, with its number and plate.
	/// </summary>
	Card = 4,
}
