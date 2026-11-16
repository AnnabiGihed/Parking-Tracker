namespace ParkingTracker.Domain.Geometry;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Small-distance geometry on the sphere and in the plane: the distance between two positions and the
///              point-in-ring test.
/// </summary>
public static class GeoMath
{
	#region Constants
	/// <summary>
	/// Mean Earth radius in metres used for distances.
	/// </summary>
	private const double EarthRadiusMetres = 6371000;
	#endregion

	#region Public Methods
	/// <summary>
	/// Computes the distance between two positions with the equirectangular approximation, which is accurate over
	/// the few hundred metres this app works with.
	/// </summary>
	/// <param name="from">The first position.</param>
	/// <param name="to">The second position.</param>
	/// <returns>The distance in metres.</returns>
	public static double Distance(GeoPoint from, GeoPoint to)
	{
		const double radians = WebMercator.RadiansPerDegree;
		var x = (to.Longitude - from.Longitude) * radians * Math.Cos((from.Latitude + to.Latitude) / 2 * radians);
		var y = (to.Latitude - from.Latitude) * radians;
		return Math.Sqrt((x * x) + (y * y)) * EarthRadiusMetres;
	}

	/// <summary>
	/// Computes the straight-line distance between two points in the Web Mercator plane.
	/// </summary>
	/// <param name="from">The first point.</param>
	/// <param name="to">The second point.</param>
	/// <returns>The distance in Mercator metres (not scaled to the latitude).</returns>
	public static double Distance(MercatorPoint from, MercatorPoint to)
	{
		var dx = to.X - from.X;
		var dy = to.Y - from.Y;
		return Math.Sqrt((dx * dx) + (dy * dy));
	}

	/// <summary>
	/// Determines whether a point lies inside a ring with the even-odd rule.
	/// </summary>
	/// <param name="point">The point.</param>
	/// <param name="ring">The ring, as consecutive vertices.</param>
	/// <returns><see langword="true"/> when the point is inside the ring.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="ring"/> is <see langword="null"/>.</exception>
	public static bool InRing(MercatorPoint point, IReadOnlyList<MercatorPoint> ring)
	{
		ArgumentNullException.ThrowIfNull(ring);
		var inside = false;
		for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
		{
			var (xi, yi) = (ring[i].X, ring[i].Y);
			var (xj, yj) = (ring[j].X, ring[j].Y);
			if ((yi > point.Y) != (yj > point.Y) && point.X < ((xj - xi) * (point.Y - yi) / (yj - yi)) + xi)
			{
				inside = !inside;
			}
		}

		return inside;
	}
	#endregion
}
