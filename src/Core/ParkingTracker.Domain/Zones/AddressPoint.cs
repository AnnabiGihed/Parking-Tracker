using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Geometry;

namespace ParkingTracker.Domain.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : An official UrbIS address point: the house number on the façade of a building, not yet tied to a street surface.
/// </summary>
/// <param name="Text">The house number as published, for example <c>216</c> or <c>12b</c>.</param>
/// <param name="House">The parsed house number.</param>
/// <param name="Position">Where the address point is, in Web Mercator.</param>
public sealed record AddressPoint(string Text, HouseNumber House, MercatorPoint Position);
