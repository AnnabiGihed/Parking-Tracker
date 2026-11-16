using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Naming;
using ParkingTracker.Domain.Zones;

namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Ties each official address point to the street surface it faces (the nearest one). Corner addresses that are
///              about as close to another street, addresses far from every surface and numbers that do not fit their
///              neighbours are left out, so that a house number is only used for the street it really belongs to.
/// </summary>
public static class AddressAssigner
{
	#region Constants
	/// <summary>
	/// An address about as close to two different streets (within this many metres) is left out.
	/// </summary>
	public const double CornerAmbiguityMetres = 4;

	/// <summary>
	/// A house number belongs to a street surface if it is at most this close, in metres.
	/// </summary>
	public const double AddressToSurfaceMetres = 35;

	private const int NeighbourCount = 3;
	private const int MaxNeighbourNumberGap = 20;
	#endregion

	#region Public Methods
	/// <summary>
	/// Assigns address points to street surfaces.
	/// </summary>
	/// <param name="points">The official address points around the place.</param>
	/// <param name="zones">The street surfaces around the place.</param>
	/// <param name="sector">The sector whose street list gives each street its index; <see langword="null"/> gives -1.</param>
	/// <returns>The addresses that could be assigned with confidence.</returns>
	/// <exception cref="ArgumentNullException">An argument other than <paramref name="sector"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<AssignedAddress> Assign(IReadOnlyList<AddressPoint> points, IReadOnlyList<ZoneSurface> zones, SectorModel? sector)
	{
		ArgumentNullException.ThrowIfNull(points);
		ArgumentNullException.ThrowIfNull(zones);
		var streetNames = zones.Select(zone => NameNormalizer.Normalize(zone.FrenchName)).ToList();
		var assigned = new List<AssignedAddress>();
		foreach (var point in points)
		{
			var address = AssignOne(point, zones, streetNames, sector);
			if (address is not null)
			{
				assigned.Add(address);
			}
		}

		return assigned.Where(address => !IsOutlier(address, assigned)).ToList();
	}
	#endregion

	#region Private Helpers
	private static AssignedAddress? AssignOne(AddressPoint point, IReadOnlyList<ZoneSurface> zones, List<string> streetNames, SectorModel? sector)
	{
		var position = WebMercator.ToGeo(point.Position);
		var scale = Math.Cos(position.Latitude * WebMercator.RadiansPerDegree);
		var bestIndex = -1;
		double best = double.PositiveInfinity, other = double.PositiveInfinity;
		for (var i = 0; i < zones.Count; i++)
		{
			var bounds = zones[i].Bounds;
			var lowerBound = Math.Sqrt(Square(Math.Max(Math.Max(bounds.MinX - point.Position.X, 0), point.Position.X - bounds.MaxX))
				+ Square(Math.Max(Math.Max(bounds.MinY - point.Position.Y, 0), point.Position.Y - bounds.MaxY))) * scale;
			if (lowerBound > other && lowerBound > best)
			{
				continue;
			}

			var metres = zones[i].Shape.ClosestTo(position).Metres;
			var differentStreet = bestIndex >= 0 && streetNames[bestIndex] != streetNames[i];
			if (metres < best)
			{
				other = differentStreet ? Math.Min(other, best) : other;
				best = metres;
				bestIndex = i;
			}
			else if (differentStreet)
			{
				other = Math.Min(other, metres);
			}
		}

		if (bestIndex < 0 || best > AddressToSurfaceMetres || other - best < CornerAmbiguityMetres)
		{
			return null;
		}

		var surface = zones[bestIndex];
		return new AssignedAddress(
			point.Text,
			point.House,
			position,
			streetNames[bestIndex],
			NameNormalizer.Normalize(surface.Municipality),
			surface.Id,
			sector?.FindStreet(surface.FrenchName, surface.DutchName)?.Index ?? -1);
	}

	private static bool IsOutlier(AssignedAddress address, List<AssignedAddress> all)
	{
		var neighbours = all
			.Where(other => !ReferenceEquals(other, address) && other.Street == address.Street)
			.Select(other => (other.House, Metres: GeoMath.Distance(address.Position, other.Position)))
			.OrderBy(entry => entry.Metres)
			.Take(NeighbourCount)
			.ToList();
		return neighbours.Count > 0 && neighbours.All(entry => Math.Abs(entry.House.Number - address.House.Number) > MaxNeighbourNumberGap);
	}

	private static double Square(double value) => value * value;
	#endregion
}
