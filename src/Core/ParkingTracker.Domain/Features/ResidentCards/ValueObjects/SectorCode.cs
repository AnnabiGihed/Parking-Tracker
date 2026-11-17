namespace ParkingTracker.Domain.Features.ResidentCards.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The identifier of the City of Brussels sector printed on a residents' card, for example <c>louise</c>.
/// </summary>
public sealed record SectorCode
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SectorCode"/> class.
	/// </summary>
	/// <param name="value">The trimmed, non-empty identifier.</param>
	private SectorCode(string value)
	{
		Value = value;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the identifier as the reference data writes it.
	/// </summary>
	public string Value { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the code of a card that may have no sector.
	/// </summary>
	/// <param name="text">The identifier, or <see langword="null"/> or blank when the card has no sector.</param>
	/// <returns>The code, or <see langword="null"/> when there is none.</returns>
	public static SectorCode? CreateOptional(string? text)
	{
		return string.IsNullOrWhiteSpace(text) ? null : new SectorCode(text.Trim());
	}
	#endregion
}
