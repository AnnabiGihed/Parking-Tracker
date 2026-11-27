namespace ParkingTracker.ViewModels.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Where the profile sync stands (ADR-0032), as the menu shows it and the sign-out decides on it.
/// </summary>
public enum ProfileSyncState
{
	/// <summary>
	/// The phone and the account agree: nothing is waiting to be uploaded.
	/// </summary>
	Synced = 0,

	/// <summary>
	/// A change is waiting to be uploaded, or the sync did not run yet.
	/// </summary>
	Pending = 1,

	/// <summary>
	/// A sync is running.
	/// </summary>
	Syncing = 2,

	/// <summary>
	/// The server could not be reached; the changes stay on the phone and are uploaded when the network returns.
	/// </summary>
	Offline = 3,

	/// <summary>
	/// The sync stopped because of a failure that waiting does not fix right away (a refusal, a server fault, a saved value that cannot be used).
	/// </summary>
	Error = 4,
}
