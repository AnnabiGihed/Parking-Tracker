using ParkingTracker.Domain.Geometry;

namespace ParkingTracker.Domain.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One street surface of the parking.brussels regulation zones: its street, municipality, zone colour and shape.
///              Tunnels, viaducts and disabled surfaces are left out before a surface is created.
/// </summary>
/// <param name="Id">Identifier of the surface; two surfaces with the same identifier are the same surface.</param>
/// <param name="FrenchName">The street name in French.</param>
/// <param name="DutchName">The street name in Dutch.</param>
/// <param name="Municipality">The municipality name exactly as parking.brussels writes it.</param>
/// <param name="ColourCode">The <c>typreg</c> colour code, or <see langword="null"/> when missing.</param>
/// <param name="ZoneCode">The code of the zone's parking rule (<c>ZoneId1</c>), or <see langword="null"/> when missing.</param>
/// <param name="Shape">The shape of the surface in Web Mercator.</param>
public sealed record ZoneSurface(
	string? Id,
	string FrenchName,
	string DutchName,
	string Municipality,
	int? ColourCode,
	string? ZoneCode,
	SurfaceShape Shape)
{
	#region Properties
	/// <summary>
	/// Gets the bounding box of the outer rings, computed once.
	/// </summary>
	public MercatorBounds Bounds { get; } = Shape.Bounds;

	/// <summary>
	/// Gets the geographic centre of <see cref="Bounds"/>.
	/// </summary>
	public GeoPoint BoundsCenter => WebMercator.ToGeo(new MercatorPoint((Bounds.MinX + Bounds.MaxX) / 2, (Bounds.MinY + Bounds.MaxY) / 2));
	#endregion
}
