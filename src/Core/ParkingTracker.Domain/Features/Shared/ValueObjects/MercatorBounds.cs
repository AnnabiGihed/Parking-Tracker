using System.Runtime.InteropServices;

namespace ParkingTracker.Domain.Features.Shared.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The smallest axis-aligned box, in Web Mercator metres, that contains the outline of a surface.
/// </summary>
/// <param name="MinX">Smallest X.</param>
/// <param name="MinY">Smallest Y.</param>
/// <param name="MaxX">Largest X.</param>
/// <param name="MaxY">Largest Y.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MercatorBounds(double MinX, double MinY, double MaxX, double MaxY);
