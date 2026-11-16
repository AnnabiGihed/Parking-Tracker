namespace ParkingTracker.Domain.ReferenceData;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : How a municipality's residents' card works, which decides how the app checks whether a card covers a spot.
/// </summary>
public enum MunicipalityKind
{
	/// <summary>
	/// Sectors are defined by street and house-number lists (City of Brussels).
	/// </summary>
	Streets,

	/// <summary>
	/// The card lists one or more mailles (areas) whose shapes parking.brussels publishes.
	/// </summary>
	Mailles,

	/// <summary>
	/// No sectors: the card is valid in the card zones of the whole municipality.
	/// </summary>
	Whole,

	/// <summary>
	/// Woluwe-Saint-Lambert: blue zones everywhere, green and grey zones only in the neighbourhood printed on the card.
	/// </summary>
	Wsl,

	/// <summary>
	/// Sectors exist but their boundaries are not published, so the app asks the user to check the card.
	/// </summary>
	Unknown,
}
