using ParkingTracker.Domain.Features.Zones.ValueObjects;

namespace ParkingTracker.Application.Features.Sectors.Queries.GetMailles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The areas the user can tick on the card form for one municipality.
/// </summary>
/// <param name="Areas">The areas whose code starts with a prefix of the municipality, sorted by name.</param>
/// <param name="BorderZones">The border zones ("limitrophes") of the municipality.</param>
public sealed record GetMaillesResponse(IReadOnlyList<MailleOption> Areas, IReadOnlyList<MailleOption> BorderZones);
