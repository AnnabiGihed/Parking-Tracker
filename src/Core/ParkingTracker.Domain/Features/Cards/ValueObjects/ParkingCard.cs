namespace ParkingTracker.Domain.Features.Cards.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The residents' parking card of the user, as far as a decision needs it: where it is valid and for how long.
///              The card number and plate are kept as the user typed them; no decision reads them.
/// </summary>
/// <param name="Municipality">Identifier of the municipality that issued the card, for example <c>bruxelles</c>.</param>
/// <param name="Sector">Identifier of the City of Brussels sector, or <see langword="null"/> for other municipalities.</param>
/// <param name="Period">The validity dates.</param>
/// <param name="Tolerances">Codes of the tolerance agreements printed on the card.</param>
/// <param name="Mailles">Zone codes of the areas (mailles) printed on the card.</param>
/// <param name="Lims">Zone codes of the border zones ("limitrophes") on the card.</param>
/// <param name="Number">The card number, or <see langword="null"/> when none was entered.</param>
/// <param name="Plate">The number plate in capitals without spaces or dashes, or <see langword="null"/> when none was entered.</param>
public sealed record ParkingCard(
	string Municipality,
	string? Sector,
	CardPeriod Period,
	IReadOnlyList<string> Tolerances,
	IReadOnlyList<string> Mailles,
	IReadOnlyList<string> Lims,
	string? Number = null,
	string? Plate = null);
