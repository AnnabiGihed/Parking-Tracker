namespace ParkingTracker.ViewModels.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The colour of the dot on the card button.
/// </summary>
public enum CardDot
{
	/// <summary>
	/// There is no card yet.
	/// </summary>
	None,

	/// <summary>
	/// The card is valid and the street lists are confirmed.
	/// </summary>
	Ok,

	/// <summary>
	/// The card is not valid today, or the street lists may be outdated.
	/// </summary>
	Bad,
}
