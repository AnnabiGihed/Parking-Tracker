using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;

namespace ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The best match of an address search.
/// </summary>
/// <param name="Match">The best match, or <see langword="null"/> when the address was not found.</param>
public sealed record SearchAddressResponse(AddressMatch? Match);
