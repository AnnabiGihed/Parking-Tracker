namespace ParkingTracker.ViewModels.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Normalises the plate while the user types it, so the field always shows capitals. The domain licence plate keeps its
///              own normalisation when the card is saved.
/// </summary>
public static class PlateInput
{
	#region Public Methods
	/// <summary>
	/// Writes the typed plate in capitals, independent of the language of the phone.
	/// </summary>
	/// <param name="typed">The text in the field, or <see langword="null"/>.</param>
	/// <returns>The text in capitals; an empty text for <see langword="null"/>.</returns>
	public static string Normalise(string? typed) => typed?.ToUpperInvariant() ?? string.Empty;
	#endregion
}
