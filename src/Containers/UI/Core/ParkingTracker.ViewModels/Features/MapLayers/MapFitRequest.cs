using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.ViewModels.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A request from a view model to show two places on the map at once, for example where the user is and the nearest
///              free spot.
/// </summary>
/// <param name="First">The first place.</param>
/// <param name="Second">The second place.</param>
public sealed record MapFitRequest(GeoPoint First, GeoPoint Second);
