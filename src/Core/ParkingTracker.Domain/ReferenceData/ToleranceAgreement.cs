namespace ParkingTracker.Domain.ReferenceData;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A tolerance agreement: streets on a neighbouring municipality's territory that are open to the sector's
///              residents, valid only when the card carries the code.
/// </summary>
/// <param name="Code">Code the card carries, for example <c>BXL_LIM_IXL_LOUISE</c>.</param>
/// <param name="Label">Display label.</param>
/// <param name="Commune">Neighbouring municipality on whose territory the agreement applies.</param>
/// <param name="Streets">Covered streets; an empty cell or <c>hors</c> means the side is not covered
/// (<see cref="SectorStreet.EmptyMeansWholeSide"/> is <see langword="false"/> for these rows).</param>
public sealed record ToleranceAgreement(
	string Code,
	string Label,
	string Commune,
	IReadOnlyList<SectorStreet> Streets);
