namespace ParkingTracker.Domain.Features.Sectors.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A residents' parking sector defined by street lists, with the official documents it was verified
///              against and any tolerance agreements.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>louise</c>.</param>
/// <param name="Name">French display name.</param>
/// <param name="NameNl">Dutch display name.</param>
/// <param name="Municipality">Identifier of the municipality that owns the sector, or <see langword="null"/> when the data does not say.</param>
/// <param name="Area">Bounds that contain the sector, or <see langword="null"/> when the data does not give them.</param>
/// <param name="Sources">Fingerprints of the official documents the list was verified against.</param>
/// <param name="Streets">The streets of the sector.</param>
/// <param name="Tolerances">Tolerance agreements with neighbouring municipalities; empty when there are none.</param>
public sealed record SectorDefinition(
	string Id,
	string Name,
	string NameNl,
	string? Municipality,
	SectorArea? Area,
	IReadOnlyList<SourceFingerprint> Sources,
	IReadOnlyList<SectorStreet> Streets,
	IReadOnlyList<ToleranceAgreement> Tolerances);
