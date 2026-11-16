namespace ParkingTracker.Domain.Geometry;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A position in Web Mercator (EPSG:3857) metres, the projection parking.brussels returns its surfaces in.
/// </summary>
/// <param name="X">Metres east of the origin.</param>
/// <param name="Y">Metres north of the origin.</param>
public readonly record struct MercatorPoint(double X, double Y);
