using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;

namespace ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The matches of an address search.
/// </summary>
/// <param name="Matches">The matches, best first; empty when the address was not found.</param>
public sealed record SearchAddressResponse(IReadOnlyList<AddressMatch> Matches);
