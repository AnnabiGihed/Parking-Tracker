namespace ParkingTracker.ViewModels.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What a request for a sync came back with (ADR-0032): whether a sync actually ran and where it ended. A request that did not run
///              (the owner is not verified, so nothing was read or sent) carries the state the service had before, which may be a
///              <see cref="ProfileSyncState.Synced"/> from an earlier run; only <see cref="IsSaved"/> says that the phone and the account agree
///              right now, so the sign-out relies on it and on nothing else.
/// </summary>
/// <param name="Ran">Whether a sync ran for this request.</param>
/// <param name="State">The state the sync ended in, or the state of the service when none ran.</param>
public sealed record ProfileSyncResult(bool Ran, ProfileSyncState State)
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether a sync ran and ended synced.
	/// </summary>
	public bool IsSaved => Ran && State == ProfileSyncState.Synced;
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the result of a request that did not run a sync.
	/// </summary>
	/// <param name="state">The state of the service.</param>
	/// <returns>The result.</returns>
	public static ProfileSyncResult NotRun(ProfileSyncState state) => new(false, state);
	#endregion
}
