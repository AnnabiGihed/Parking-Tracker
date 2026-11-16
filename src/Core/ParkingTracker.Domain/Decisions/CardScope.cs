namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Whether the card covers a place, for zones that accept residents' cards, with the reason in words.
/// </summary>
/// <param name="State">The answer.</param>
/// <param name="Why">The reason, to be completed to a sentence by the caller; <see langword="null"/> for <see cref="CardScopeState.Streets"/>.</param>
public sealed record CardScope(CardScopeState State, string? Why)
{
	#region Factory Methods
	/// <summary>
	/// Creates the answer "the card covers this place".
	/// </summary>
	/// <param name="why">The reason.</param>
	/// <returns>The scope.</returns>
	public static CardScope Yes(string why) => new(CardScopeState.Yes, why);

	/// <summary>
	/// Creates the answer "the card does not cover this place".
	/// </summary>
	/// <param name="why">The reason.</param>
	/// <returns>The scope.</returns>
	public static CardScope No(string why) => new(CardScopeState.No, why);

	/// <summary>
	/// Creates the answer "the user has to check".
	/// </summary>
	/// <param name="why">The reason.</param>
	/// <returns>The scope.</returns>
	public static CardScope Check(string why) => new(CardScopeState.Check, why);

	/// <summary>
	/// Creates the answer "decide with the City street lists".
	/// </summary>
	/// <returns>The scope.</returns>
	public static CardScope Streets() => new(CardScopeState.Streets, null);
	#endregion
}
