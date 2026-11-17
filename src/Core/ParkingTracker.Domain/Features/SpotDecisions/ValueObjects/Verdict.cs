namespace ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The headline answer to "may I park here with my card?".
/// </summary>
public enum Verdict
{
	/// <summary>
	/// Free with your card.
	/// </summary>
	Free,

	/// <summary>
	/// Paid parking applies, but not at this moment.
	/// </summary>
	FreeNow,

	/// <summary>
	/// You have to pay.
	/// </summary>
	Pay,

	/// <summary>
	/// A reserved space: nobody may park here whatever the card.
	/// </summary>
	Reserved,

	/// <summary>
	/// Parking is free with your card, but only for a limited time.
	/// </summary>
	TimeLimited,

	/// <summary>
	/// A blue zone not covered by the card: a parking disc is needed.
	/// </summary>
	ParkingDisc,

	/// <summary>
	/// The card covers part of the street: look at the house number in front of you.
	/// </summary>
	CheckNumber,

	/// <summary>
	/// The rules of the card cannot be applied automatically: check your card.
	/// </summary>
	CheckCard,

	/// <summary>
	/// The answer depends on a condition in words: follow the signs.
	/// </summary>
	CheckSigns,

	/// <summary>
	/// The answer could not be confirmed with live data.
	/// </summary>
	CantVerify,
}
