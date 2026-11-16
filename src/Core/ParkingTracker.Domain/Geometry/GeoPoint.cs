namespace ParkingTracker.Domain.Geometry;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A position on Earth in degrees (WGS 84). Range checks happen where coordinates enter the system.
/// </summary>
/// <param name="Latitude">Latitude in degrees, north positive.</param>
/// <param name="Longitude">Longitude in degrees, east positive.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude);
