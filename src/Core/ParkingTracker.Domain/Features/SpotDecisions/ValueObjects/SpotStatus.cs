namespace ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The colour of an answer on the screen: green, red or grey.
/// </summary>
public enum SpotStatus
{
	/// <summary>
	/// Parking is free for you now.
	/// </summary>
	Free,

	/// <summary>
	/// You cannot park for free here.
	/// </summary>
	Pay,

	/// <summary>
	/// The answer is uncertain.
	/// </summary>
	Unknown,
}
