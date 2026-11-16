namespace ParkingTracker.Domain.Addressing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One side of a street: house numbers are odd on one side and even on the other.
/// </summary>
public enum StreetSide
{
	/// <summary>
	/// The side with the odd house numbers.
	/// </summary>
	Odd,

	/// <summary>
	/// The side with the even house numbers.
	/// </summary>
	Even,
}
