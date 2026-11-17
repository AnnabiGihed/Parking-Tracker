using ParkingTracker.Domain.Features.Sectors.Services;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Zones.ValueObjects;

namespace ParkingTracker.Domain.Features.SpotDecisions.Services;

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

	/// <summary>
	/// The number of neighbours a number is compared with.
	/// </summary>
	private const int NeighbourCount = 3;

	/// <summary>
	/// The largest gap between a number and its neighbours before it is an outlier.
	/// </summary>
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
	/// <summary>
	/// Assigns one address point to the street it most likely belongs to.
	/// </summary>
	/// <param name="point">Address point to assign to a surface.</param>
	/// <param name="zones">The zone surfaces.</param>
	/// <param name="streetNames">Street names of the surfaces the address could belong to.</param>
	/// <param name="sector">Sector of the saved card, if any.</param>
	/// <returns>The assigned address, or <see langword="null"/> when no street fits.</returns>
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
			var differentStreet = bestIndex >= 0 && !string.Equals(streetNames[bestIndex], streetNames[i], StringComparison.Ordinal);
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

	/// <summary>
	/// Determines whether an assigned address is out of line with the numbers of its neighbours.
	/// </summary>
	/// <param name="address">Address under test.</param>
	/// <param name="all">All addresses assigned in the area; the outlier is judged against them.</param>
	/// <returns><see langword="true"/> when an assigned address is out of line with the numbers of its neighbours; otherwise <see langword="false"/>.</returns>
	private static bool IsOutlier(AssignedAddress address, List<AssignedAddress> all)
	{
		var neighbours = all
			.Where(other => !ReferenceEquals(other, address) && string.Equals(other.Street, address.Street, StringComparison.Ordinal))
			.Select(other => (other.House, Metres: GeoMath.Distance(address.Position, other.Position)))
			.OrderBy(entry => entry.Metres)
			.Take(NeighbourCount)
			.ToList();
		return neighbours.Count > 0 && neighbours.All(entry => Math.Abs(entry.House.Number - address.House.Number) > MaxNeighbourNumberGap);
	}

	/// <summary>
	/// Squares a number.
	/// </summary>
	/// <param name="value">Number to square.</param>
	/// <returns>The square of the value.</returns>
	private static double Square(double value) => value * value;
	#endregion
}
