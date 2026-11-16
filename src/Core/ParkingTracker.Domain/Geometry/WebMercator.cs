namespace ParkingTracker.Domain.Geometry;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Converts between WGS 84 degrees and Web Mercator (EPSG:3857) metres.
/// </summary>
public static class WebMercator
{
	#region Constants
	/// <summary>
	/// Radians per degree.
	/// </summary>
	internal const double RadiansPerDegree = Math.PI / 180;

	/// <summary>
	/// Earth radius of the projection in metres.
	/// </summary>
	private const double EarthRadiusMetres = 6378137;
	#endregion

	#region Public Methods
	/// <summary>
	/// Projects a position to Web Mercator.
	/// </summary>
	/// <param name="point">The position in degrees.</param>
	/// <returns>The position in metres.</returns>
	public static MercatorPoint ToMercator(GeoPoint point)
	{
		return new MercatorPoint(
			point.Longitude * RadiansPerDegree * EarthRadiusMetres,
			Math.Log(Math.Tan((Math.PI / 4) + (point.Latitude * RadiansPerDegree / 2))) * EarthRadiusMetres);
	}

	/// <summary>
	/// Converts a Web Mercator position back to degrees.
	/// </summary>
	/// <param name="point">The position in metres.</param>
	/// <returns>The position in degrees.</returns>
	public static GeoPoint ToGeo(MercatorPoint point)
	{
		return new GeoPoint(
			((2 * Math.Atan(Math.Exp(point.Y / EarthRadiusMetres))) - (Math.PI / 2)) / RadiansPerDegree,
			point.X / EarthRadiusMetres / RadiansPerDegree);
	}
	#endregion
}
