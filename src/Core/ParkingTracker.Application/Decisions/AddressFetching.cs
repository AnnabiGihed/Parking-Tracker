namespace ParkingTracker.Application.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Whether the official house numbers are fetched when the surfaces of an area are evaluated.
/// </summary>
public enum AddressFetching
{
	/// <summary>
	/// House numbers are not fetched; part-covered streets are not told apart.
	/// </summary>
	Skip,

	/// <summary>
	/// House numbers are fetched for City of Brussels cards, to colour part-covered streets and to find the free side.
	/// </summary>
	Fetch,
}
