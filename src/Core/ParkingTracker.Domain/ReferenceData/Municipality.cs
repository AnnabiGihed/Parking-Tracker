namespace ParkingTracker.Domain.ReferenceData;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One of the 19 municipalities of the Brussels-Capital Region and how its residents' card works.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>saint-gilles</c>.</param>
/// <param name="Name">French display name.</param>
/// <param name="NameNl">Dutch display name.</param>
/// <param name="Mu">The municipality name exactly as parking.brussels writes it.</param>
/// <param name="Kind">How the residents' card works in this municipality.</param>
/// <param name="Prefixes">Zone-code prefixes of the mailles (only for <see cref="MunicipalityKind.Mailles"/>); empty otherwise.</param>
/// <param name="Lims">Codes of the tolerance agreements ("limitrophes") with neighbouring municipalities; empty when none.</param>
/// <param name="Note">Explanation shown to the user when the card cannot be checked automatically, or <see langword="null"/>.</param>
public sealed record Municipality(
	string Id,
	string Name,
	string NameNl,
	string Mu,
	MunicipalityKind Kind,
	IReadOnlyList<string> Prefixes,
	IReadOnlyList<string> Lims,
	string? Note);
