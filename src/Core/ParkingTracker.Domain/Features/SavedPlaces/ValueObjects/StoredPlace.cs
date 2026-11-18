using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One saved place as it was stored, with the identifier it had, so that <see cref="Aggregates.SavedPlaces.Restore"/> can
///              rebuild the list without giving the places new identifiers.
/// </summary>
/// <param name="Id">The stored identifier of the place.</param>
/// <param name="Kind">The kind of place.</param>
/// <param name="Label">The label the user gave the place.</param>
/// <param name="Point">Where the place is.</param>
/// <param name="AddressText">The address in words; <see langword="null"/> or empty when unknown.</param>
public sealed record StoredPlace(SavedPlaceId Id, PlaceKind Kind, string? Label, GeoPoint Point, string? AddressText);
