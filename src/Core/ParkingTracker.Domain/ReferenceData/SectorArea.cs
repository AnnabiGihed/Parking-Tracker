namespace ParkingTracker.Domain.ReferenceData;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The rectangle, as south, west, north and east bounds in degrees, that contains all streets of a sector.
/// </summary>
/// <param name="South">Southern latitude.</param>
/// <param name="West">Western longitude.</param>
/// <param name="North">Northern latitude.</param>
/// <param name="East">Eastern longitude.</param>
public sealed record SectorArea(double South, double West, double North, double East);
