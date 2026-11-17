namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the <c>ptTest.screenState</c> hook returns to the emulator test: what the main screen holds, so that a failed
///              check of the position or the answer says why (no fix, a fix that is too old, no tracking, a check that never ended).
/// </summary>
/// <param name="Tracking">Whether the position of the user is followed.</param>
/// <param name="Me">The last usable fix of the user, or <see langword="null"/> when none was accepted.</param>
/// <param name="Title">The headline of the status block.</param>
/// <param name="Sub">The explanation under the headline.</param>
/// <param name="Busy">Whether a check is running or a busy chip is shown.</param>
public sealed record ScreenStateAnswer(bool Tracking, MyPositionAnswer? Me, string Title, string Sub, bool Busy);
