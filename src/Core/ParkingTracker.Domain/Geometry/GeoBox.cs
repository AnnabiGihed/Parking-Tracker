namespace ParkingTracker.Domain.Geometry;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A latitude/longitude rectangle used to ask a live source for the features around a spot.
/// </summary>
/// <param name="South">Southern latitude.</param>
/// <param name="West">Western longitude.</param>
/// <param name="North">Northern latitude.</param>
/// <param name="East">Eastern longitude.</param>
public readonly record struct GeoBox(double South, double West, double North, double East)
{
	#region Constants
	/// <summary>
	/// Default growth of a box in degrees of latitude, about 45 m.
	/// </summary>
	public const double DefaultPadding = 0.0004;

	/// <summary>
	/// Longitude degrees are this much shorter than latitude degrees at Brussels, so the box grows by this factor more east and west.
	/// </summary>
	private const double LongitudeFactor = 1.6;
	#endregion

	#region Public Methods
	/// <summary>
	/// Grows the box so that addresses near its edge still see the streets they face.
	/// </summary>
	/// <param name="padding">Growth in degrees of latitude; longitude grows 1.6 times as much.</param>
	/// <returns>The grown box.</returns>
	public GeoBox Pad(double padding = DefaultPadding)
	{
		return new GeoBox(South - padding, West - (padding * LongitudeFactor), North + padding, East + (padding * LongitudeFactor));
	}
	#endregion
}
