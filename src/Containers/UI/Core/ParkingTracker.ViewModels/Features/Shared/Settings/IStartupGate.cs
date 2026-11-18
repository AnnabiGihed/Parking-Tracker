namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the settings store needs from the start-up gate: to wait for it before it reads the card, the car or the timer,
///              so that a reader that runs before the screens (the background service, the boot receiver) never caches "no card" while
///              the old values are still being moved into the database, and to know whether the one-time move has finished, so that a
///              change made before then also removes the old copy and the next launch does not import deleted data again.
/// </summary>
public interface IStartupGate
{
	#region Methods
	/// <summary>
	/// Runs the gate once in this process, or returns the run that started already; it never throws.
	/// </summary>
	/// <returns>A task that completes when the saved values can be read.</returns>
	Task RunOnceAsync();

	/// <summary>
	/// Tells whether the one-time move of the old card, car and timer into the database has finished (moved, nothing to move, or given up).
	/// </summary>
	/// <returns>A task with <see langword="false"/> while the move is still to be tried at the next launch.</returns>
	Task<bool> IsMoveFinishedAsync();
	#endregion
}
