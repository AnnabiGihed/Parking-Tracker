namespace ParkingTracker.Domain.Pricing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What kind of zone a street surface is for a residents' card.
/// </summary>
public enum ZoneType
{
	/// <summary>
	/// No parking rules: free for everyone.
	/// </summary>
	Free,

	/// <summary>
	/// Paid parking where a residents' card is never valid.
	/// </summary>
	Payable,

	/// <summary>
	/// Paid parking that a residents' card waives in its own sector.
	/// </summary>
	Derogation,

	/// <summary>
	/// Blue zone (parking disc); a residents' card is valid in its own sector.
	/// </summary>
	Disk,

	/// <summary>
	/// Spaces reserved for residents.
	/// </summary>
	Residents,

	/// <summary>
	/// Reserved use (delivery, car sharing…): a residents' card is not valid.
	/// </summary>
	Reserved,
}
