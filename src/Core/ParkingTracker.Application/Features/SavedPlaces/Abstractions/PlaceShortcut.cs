using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Application.Features.SavedPlaces.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A saved Home or Work place as the shortcut chips show it: what it is, where it is and the address in words.
/// </summary>
/// <param name="Kind">Home or work.</param>
/// <param name="Label">The label of the place, which is the name of its kind.</param>
/// <param name="Point">Where the place is.</param>
/// <param name="AddressText">The address in words; empty when unknown.</param>
public sealed record PlaceShortcut(PlaceKind Kind, string Label, GeoPoint Point, string AddressText);
