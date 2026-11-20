using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Application.Features.SavedPlaces.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A saved place as the shortcut chips and the favourites list show it: which one it is, what it is, where it is and the address in words.
/// </summary>
/// <param name="Id">The identifier of the place, used to change or remove a favourite.</param>
/// <param name="Kind">Home, work or favourite.</param>
/// <param name="Label">The label of the place: the name of its kind for home and work, the name the user gave for a favourite.</param>
/// <param name="Point">Where the place is.</param>
/// <param name="AddressText">The address in words; empty when unknown.</param>
public sealed record PlaceShortcut(SavedPlaceId Id, PlaceKind Kind, string Label, GeoPoint Point, string AddressText);
