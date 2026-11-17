namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the <c>ptTest.officialList</c> hook returns to the emulator test: the outcome of the check that the City of
///              Brussels did not change its official sector document.
/// </summary>
/// <param name="State">The outcome: <c>ok</c>, <c>changed</c>, <c>unverified</c> or <see cref="NotCheckedState"/>.</param>
/// <param name="Problem">What to tell the user when the state is not <c>ok</c>; <see langword="null"/> otherwise.</param>
public sealed record OfficialListAnswer(string State, string? Problem)
{
	#region Constants
	/// <summary>
	/// The state when no check ran yet, for example because the card is not a City of Brussels card.
	/// </summary>
	public const string NotCheckedState = "notchecked";
	#endregion
}
