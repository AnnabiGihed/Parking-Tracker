using ParkingTracker.Domain.Features.Sectors.ValueObjects;

namespace ParkingTracker.Application.Features.Sectors.Queries.GetOfficialListStatus;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The state of the official City of Brussels sector document.
/// </summary>
/// <param name="Status">The state, or <see langword="null"/> when the card's municipality does not use the street lists.</param>
public sealed record GetOfficialListStatusResponse(OfficialListStatus? Status);
