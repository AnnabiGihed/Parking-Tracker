namespace ParkingTracker.Domain.Features.Sectors.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A street of a sector with the house numbers of each side that belong to it, exactly as printed in the official list.
/// </summary>
/// <param name="FrenchName">Street name in French.</param>
/// <param name="DutchName">Street name in Dutch.</param>
/// <param name="Odd">
/// Rule text for the odd side: empty = the whole side or not covered (see <paramref name="EmptyMeansWholeSide"/>), <c>hors</c> = not covered, <c>17-57b</c> = a range,
/// <c>59 à fin</c> = from 59 to the end of the street, <c>2-6 et 64-76</c> = two ranges, <c>check: …</c> = a condition in words.
/// </param>
/// <param name="Even">Rule text for the even side, with the same syntax as <paramref name="Odd"/>.</param>
/// <param name="Postcode">Postcode when the list gives one, otherwise <see langword="null"/>.</param>
/// <param name="EmptyMeansWholeSide">
/// How to read an empty side cell: <see langword="true"/> for a row of a sector's street list (nothing listed = the whole
/// side is covered), <see langword="false"/> for a row of a tolerance agreement (nothing listed = the side is not covered).
/// The reader sets it from where the row comes from; never decide it from the cell text.
/// </param>
public sealed record SectorStreet(
	string FrenchName,
	string DutchName,
	string Odd,
	string Even,
	string? Postcode,
	bool EmptyMeansWholeSide);
