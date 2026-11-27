namespace ParkingTracker.ViewModels.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The profile sync as the account flow, the menu and the triggers use it (ADR-0032): ask for a sync and wait for its
///              outcome, ask for one without waiting, and read the state with a change event.
/// </summary>
public interface IProfileSync
{
	#region Events
	/// <summary>
	/// Raised when <see cref="State"/> changed.
	/// </summary>
	event EventHandler? Changed;
	#endregion

	#region Properties
	/// <summary>
	/// Gets where the sync stands.
	/// </summary>
	ProfileSyncState State { get; }
	#endregion

	#region Methods
	/// <summary>
	/// Runs a sync and waits for it; a call made while a sync runs joins that run, and makes the run go through one more pass when it ends,
	/// so that a change made after the pass began is never left for the next trigger. Nothing is sent unless the data on the phone is known
	/// to belong to the signed-in account.
	/// </summary>
	/// <param name="cancellationToken">Stops the wait of this caller; the sync itself goes on, so that one slow server does not hold a caller.</param>
	/// <returns>A task with whether a sync ran and the state it ended in.</returns>
	/// <exception cref="OperationCanceledException">The token was cancelled before the sync ended.</exception>
	Task<ProfileSyncResult> SyncAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Starts a sync in the background and returns at once; a failure is logged and shown in <see cref="State"/>.
	/// </summary>
	void Request();

	/// <summary>
	/// Forgets the state of the last sync and goes back to <see cref="ProfileSyncState.Pending"/>, as after a sign-out, so that no screen shows
	/// the state of an account that is gone.
	/// </summary>
	void Reset();
	#endregion
}
