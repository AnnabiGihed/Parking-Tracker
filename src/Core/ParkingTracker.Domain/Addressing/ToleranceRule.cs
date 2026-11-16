namespace ParkingTracker.Domain.Addressing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The rules one tolerance agreement gives for a street on a neighbouring municipality's territory.
/// </summary>
/// <param name="Code">Code the card must carry for the agreement to apply.</param>
/// <param name="Label">Display label of the agreement.</param>
/// <param name="Commune">Municipality on whose territory the agreement applies.</param>
/// <param name="Odd">Rule of the odd side.</param>
/// <param name="Even">Rule of the even side.</param>
public sealed record ToleranceRule(string Code, string Label, string Commune, SideRule Odd, SideRule Even);
