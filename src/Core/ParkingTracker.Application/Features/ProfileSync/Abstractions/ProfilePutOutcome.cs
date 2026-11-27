namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Which answer a write of the profile got.
/// </summary>
public enum ProfilePutOutcome
{
	/// <summary>
	/// The document was stored.
	/// </summary>
	Saved = 0,

	/// <summary>
	/// 412: another writer got there first; read again and merge.
	/// </summary>
	Stale = 1,

	/// <summary>
	/// 400, 413 or 428: the server will refuse the same document again.
	/// </summary>
	Rejected = 2,
}
