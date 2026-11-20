namespace ParkingTracker.ViewModels.Features.Places;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The steps of the map picker that sets Home or Work (ADR-0030).
/// </summary>
public enum PlacePickerState
{
	/// <summary>
	/// The picker is closed and the map shows the normal screen.
	/// </summary>
	Idle,

	/// <summary>
	/// The picker is open and no place is chosen yet: the user searches an address, uses the position or taps the map.
	/// </summary>
	Searching,

	/// <summary>
	/// A search found several places and the list waits for a pick.
	/// </summary>
	Results,

	/// <summary>
	/// A place is chosen and shown with a pin that can be dragged; nothing is saved until the user confirms.
	/// </summary>
	Confirming,

	/// <summary>
	/// The confirmed place is being written.
	/// </summary>
	Saving,
}
