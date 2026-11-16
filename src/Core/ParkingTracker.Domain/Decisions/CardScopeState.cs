namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The possible answers to "does my card cover this place?".
/// </summary>
public enum CardScopeState
{
	/// <summary>
	/// The card covers the place.
	/// </summary>
	Yes,

	/// <summary>
	/// The card does not cover the place.
	/// </summary>
	No,

	/// <summary>
	/// It cannot be told automatically: the user has to check.
	/// </summary>
	Check,

	/// <summary>
	/// The answer comes from the City of Brussels street lists.
	/// </summary>
	Streets,
}
