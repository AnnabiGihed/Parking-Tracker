using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Geometry;

namespace ParkingTracker.Domain.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : An address point tied to the street surface it faces.
/// </summary>
/// <param name="Text">The house number as published, trimmed.</param>
/// <param name="House">The parsed house number.</param>
/// <param name="Position">Where the address is.</param>
/// <param name="Street">The normalised French name of the street surface it faces.</param>
/// <param name="Commune">The normalised municipality of that surface.</param>
/// <param name="SurfaceId">The identifier of that surface.</param>
/// <param name="StreetIndex">The index of the street in the card's sector, or -1 when the sector does not list it.</param>
public sealed record AssignedAddress(
	string Text,
	HouseNumber House,
	GeoPoint Position,
	string Street,
	string Commune,
	string? SurfaceId,
	int StreetIndex);
