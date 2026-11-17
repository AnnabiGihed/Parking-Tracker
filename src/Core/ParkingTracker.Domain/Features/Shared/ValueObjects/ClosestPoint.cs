using System.Runtime.InteropServices;

namespace ParkingTracker.Domain.Features.Shared.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The point of a surface closest to a given position and the distance to it.
/// </summary>
/// <param name="Metres">Distance in metres; 0 when the position is inside the surface.</param>
/// <param name="Point">The closest point (the position itself when it is inside).</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ClosestPoint(double Metres, GeoPoint Point);
