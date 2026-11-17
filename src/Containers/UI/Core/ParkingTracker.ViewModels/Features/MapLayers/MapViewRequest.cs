using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.ViewModels.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A request from a view model to move the map: centre on a place, optionally at a zoom level. The component that
///              owns the map carries it out.
/// </summary>
/// <param name="Center">The place to centre on.</param>
/// <param name="Zoom">The zoom level, or <see langword="null"/> to keep the current one.</param>
/// <param name="OnlyZoomIn">Whether the zoom level is a minimum: a map that is already closer stays as it is.</param>
public sealed record MapViewRequest(GeoPoint Center, int? Zoom, bool OnlyZoomIn = false);
