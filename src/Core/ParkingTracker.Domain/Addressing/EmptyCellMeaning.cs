namespace ParkingTracker.Domain.Addressing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : How an empty side cell of an official list is read; it depends on which list the row comes from.
/// </summary>
public enum EmptyCellMeaning
{
	/// <summary>
	/// A row of a sector's street list: nothing listed means the whole side is covered.
	/// </summary>
	WholeSide,

	/// <summary>
	/// A row of a tolerance agreement: nothing listed means the side is not covered.
	/// </summary>
	NotCovered,
}
