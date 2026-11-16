namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The outcome of checking the City of Brussels' official sector documents against the ones the street lists were verified on.
/// </summary>
public enum OfficialListState
{
	/// <summary>
	/// Every document is unchanged.
	/// </summary>
	Ok,

	/// <summary>
	/// A document differs from the verified one: the street list may be outdated.
	/// </summary>
	Changed,

	/// <summary>
	/// The documents could not be checked.
	/// </summary>
	Unverified,
}
