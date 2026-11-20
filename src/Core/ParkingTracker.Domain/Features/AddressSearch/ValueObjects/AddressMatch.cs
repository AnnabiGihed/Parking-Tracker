using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Features.AddressSearch.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A street or address the search found, with where it is and the two short lines a list of matches shows.
/// </summary>
/// <param name="Point">The position of the match.</param>
/// <param name="DisplayName">The full name the service gave it, or <see langword="null"/>.</param>
/// <param name="Label">The short first line: road and house number, else the name of the place, or <see langword="null"/> when the service gave no name.</param>
/// <param name="Locality">The second line: postcode and municipality, or <see langword="null"/> when the service gave none.</param>
public sealed record AddressMatch(GeoPoint Point, string? DisplayName, string? Label = null, string? Locality = null);
