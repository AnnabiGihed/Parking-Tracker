using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Features.AddressSearch.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A street or address the search found, with where it is.
/// </summary>
/// <param name="Point">The position of the match.</param>
/// <param name="DisplayName">The full name the service gave it, or <see langword="null"/>.</param>
public sealed record AddressMatch(GeoPoint Point, string? DisplayName);
