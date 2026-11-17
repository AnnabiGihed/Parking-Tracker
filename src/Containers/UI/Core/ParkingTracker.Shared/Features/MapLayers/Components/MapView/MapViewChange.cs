using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Features.MapLayers.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The area and zoom level the map shows after a move, as the map script reports it.
/// </summary>
/// <param name="Bounds">The area the map shows.</param>
/// <param name="Zoom">The zoom level.</param>
public sealed record MapViewChange(MapBounds Bounds, double Zoom);
