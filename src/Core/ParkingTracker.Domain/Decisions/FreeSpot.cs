using ParkingTracker.Domain.Geometry;

namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A nearby spot where the user's card covers parking, from live data only.
/// </summary>
/// <param name="Point">The spot on the street surface, at the curb.</param>
/// <param name="Metres">The distance from where the search started.</param>
/// <param name="Street">The French name of the street.</param>
/// <param name="Commune">The municipality exactly as parking.brussels writes it.</param>
/// <param name="Label">The street, with the house number when only part of the street is covered.</param>
public sealed record FreeSpot(GeoPoint Point, double Metres, string Street, string Commune, string Label);
