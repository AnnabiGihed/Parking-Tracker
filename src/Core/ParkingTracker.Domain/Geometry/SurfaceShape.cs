namespace ParkingTracker.Domain.Geometry;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The outline of a street surface: one or more polygons, each an outer ring followed by hole rings, in
///              Web Mercator metres. It answers how far a position is from the surface.
/// </summary>
/// <param name="Polygons">The polygons; each is a list of rings and each ring a list of vertices.</param>
public sealed record SurfaceShape(IReadOnlyList<IReadOnlyList<IReadOnlyList<MercatorPoint>>> Polygons)
{
	#region Properties
	/// <summary>
	/// Gets the box around the outer rings of the polygons; its minimum is positive infinity and its maximum negative
	/// infinity when the shape has no vertices.
	/// </summary>
	public MercatorBounds Bounds
	{
		get
		{
			double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
			foreach (var polygon in Polygons.Where(p => p.Count > 0))
			{
				foreach (var vertex in polygon[0])
				{
					minX = Math.Min(minX, vertex.X);
					minY = Math.Min(minY, vertex.Y);
					maxX = Math.Max(maxX, vertex.X);
					maxY = Math.Max(maxY, vertex.Y);
				}
			}

			return new MercatorBounds(minX, minY, maxX, maxY);
		}
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Finds the point of the surface closest to a position. A position inside a polygon (and outside its holes) is at distance 0.
	/// </summary>
	/// <param name="position">The position in degrees.</param>
	/// <returns>
	/// The distance in metres (scaled to the latitude of the position) and the closest point; infinite distance and the
	/// position itself when the shape has no edges.
	/// </returns>
	public ClosestPoint ClosestTo(GeoPoint position)
	{
		var target = WebMercator.ToMercator(position);
		var bestDistance = double.PositiveInfinity;
		var best = target;
		foreach (var rings in Polygons.Where(p => p.Count > 0))
		{
			if (IsInside(target, rings))
			{
				return new ClosestPoint(0, position);
			}

			foreach (var ring in rings)
			{
				var (distance, point) = ClosestOnRing(target, ring);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = point;
				}
			}
		}

		return new ClosestPoint(bestDistance * Math.Cos(position.Latitude * WebMercator.RadiansPerDegree), WebMercator.ToGeo(best));
	}
	#endregion

	#region Private Helpers
	private static bool IsInside(MercatorPoint target, IReadOnlyList<IReadOnlyList<MercatorPoint>> rings)
	{
		return GeoMath.InRing(target, rings[0]) && !rings.Skip(1).Any(hole => GeoMath.InRing(target, hole));
	}

	private static (double Distance, MercatorPoint Point) ClosestOnRing(MercatorPoint target, IReadOnlyList<MercatorPoint> ring)
	{
		var bestDistance = double.PositiveInfinity;
		var best = target;
		for (var i = 1; i < ring.Count; i++)
		{
			var candidate = ClosestOnSegment(target, ring[i - 1], ring[i]);
			var distance = GeoMath.Distance(target, candidate);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = candidate;
			}
		}

		return (bestDistance, best);
	}

	private static MercatorPoint ClosestOnSegment(MercatorPoint target, MercatorPoint start, MercatorPoint end)
	{
		var dx = end.X - start.X;
		var dy = end.Y - start.Y;
		var lengthSquared = (dx * dx) + (dy * dy);
		var t = lengthSquared == 0 ? 0 : Math.Max(0, Math.Min(1, (((target.X - start.X) * dx) + ((target.Y - start.Y) * dy)) / lengthSquared));
		return new MercatorPoint(start.X + (t * dx), start.Y + (t * dy));
	}
	#endregion
}
