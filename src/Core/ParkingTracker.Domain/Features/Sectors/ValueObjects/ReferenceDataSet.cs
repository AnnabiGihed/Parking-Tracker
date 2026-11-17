namespace ParkingTracker.Domain.Features.Sectors.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : All reference data the app ships with: the municipalities, the City of Brussels sectors and the
///              sectors that carry tolerance agreements.
/// </summary>
/// <param name="Municipalities">The 19 municipalities of the Brussels-Capital Region.</param>
/// <param name="CitySectors">The City of Brussels residents' sectors, read from the official PDFs.</param>
/// <param name="ToleranceSectors">Sectors that carry tolerance agreements with neighbouring municipalities.</param>
public sealed record ReferenceDataSet(
	IReadOnlyList<Municipality> Municipalities,
	IReadOnlyList<SectorDefinition> CitySectors,
	IReadOnlyList<SectorDefinition> ToleranceSectors);
