namespace ParkingTracker.Domain.Features.ResidentCards.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The number printed on a residents' card, trimmed; no decision reads it, but the renewal e-mail quotes it. It is
///              personal data, so the device database stores it encrypted.
/// </summary>
public sealed record CardNumber
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="CardNumber"/> class.
	/// </summary>
	/// <param name="value">The trimmed, non-empty number.</param>
	private CardNumber(string value)
	{
		Value = value;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the number as typed, without surrounding white space.
	/// </summary>
	public string Value { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the number of a card that may have none.
	/// </summary>
	/// <param name="text">The number as typed, or <see langword="null"/> or blank when none was entered.</param>
	/// <returns>The number, or <see langword="null"/> when there is none.</returns>
	public static CardNumber? CreateOptional(string? text)
	{
		return string.IsNullOrWhiteSpace(text) ? null : new CardNumber(text.Trim());
	}
	#endregion
}
