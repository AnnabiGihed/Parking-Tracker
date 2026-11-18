namespace ParkingTracker.Domain.Features.ResidentCards.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The number plate of the car on a residents' card, in capitals without spaces or dashes, as the card form of the
///              first version wrote it. It is personal data, so the device database stores it encrypted.
/// </summary>
public sealed record LicencePlate
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LicencePlate"/> class.
	/// </summary>
	/// <param name="value">The normalised, non-empty plate.</param>
	private LicencePlate(string value)
	{
		Value = value;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the plate, for example <c>1ABC234</c>.
	/// </summary>
	public string Value { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the plate of a card that may have none; the text is put in capitals and loses its white space and dashes.
	/// </summary>
	/// <param name="text">The plate as typed, or <see langword="null"/> when none was entered.</param>
	/// <returns>The plate, or <see langword="null"/> when nothing is left after normalising.</returns>
	public static LicencePlate? CreateOptional(string? text)
	{
		var normalised = new string([.. (text ?? string.Empty).ToUpperInvariant().Where(character => !char.IsWhiteSpace(character) && character != '-')]);
		return normalised.Length == 0 ? null : new LicencePlate(normalised);
	}
	#endregion
}
