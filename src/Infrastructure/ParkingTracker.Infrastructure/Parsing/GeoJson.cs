using System.Text.Json;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Infrastructure.Sources;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Parsing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the GeoJSON feature collections of the live sources: the list of features and the shapes of their geometry,
///              in the Web Mercator coordinates the services are asked for (EPSG:3857).
/// </summary>
internal static class GeoJson
{
	#region Internal Methods
	/// <summary>
	/// Reads a feature collection and converts its features.
	/// </summary>
	/// <typeparam name="T">The converted type.</typeparam>
	/// <param name="json">The answer of the service.</param>
	/// <param name="source">The service, for the failure code.</param>
	/// <param name="what">The name of the data in failure messages.</param>
	/// <param name="filter">Which features are converted.</param>
	/// <param name="convert">Converts the features; it runs while the JSON document is open.</param>
	/// <returns>The converted value, or a <c>BadResponse</c> failure when the answer is not a feature collection.</returns>
	internal static Result<T> ReadFeatures<T>(string json, SourceKind source, string what, FeatureFilter filter, Func<IReadOnlyList<JsonElement>, T> convert)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			var features = JsonValues.Property(document.RootElement, "features");
			if (features.ValueKind != JsonValueKind.Array)
			{
				return Result.Failure<T>(SourceFailures.BadResponse(source, what));
			}

			var kept = features.EnumerateArray().Where(feature => filter == FeatureFilter.All || HasGeometry(feature)).ToList();
			return Result.Success(convert(kept));
		}
		catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
		{
			return Result.Failure<T>(SourceFailures.BadResponse(source, what));
		}
	}

	/// <summary>
	/// Gets the properties object of a feature.
	/// </summary>
	/// <param name="feature">The feature.</param>
	/// <returns>The properties, or an undefined element when the feature has none (every reader treats it as empty).</returns>
	internal static JsonElement Properties(JsonElement feature) => JsonValues.Property(feature, "properties");

	/// <summary>
	/// Gets the type of the geometry of a feature.
	/// </summary>
	/// <param name="feature">The feature.</param>
	/// <returns>The type, for example <c>Polygon</c>; <see langword="null"/> when there is none.</returns>
	internal static string? GeometryType(JsonElement feature) => JsonValues.Text(JsonValues.Property(feature, "geometry"), "type");

	/// <summary>
	/// Reads the shape of a polygon or multi-polygon geometry; any other geometry has no shape.
	/// </summary>
	/// <param name="feature">The feature.</param>
	/// <returns>The shape; without polygons for another geometry type.</returns>
	internal static SurfaceShape ReadShape(JsonElement feature)
	{
		var geometry = JsonValues.Property(feature, "geometry");
		var coordinates = JsonValues.Property(geometry, "coordinates");
		IReadOnlyList<IReadOnlyList<IReadOnlyList<MercatorPoint>>> polygons = GeometryType(feature) switch
		{
			"Polygon" => [ReadRings(coordinates)],
			"MultiPolygon" => coordinates.EnumerateArray().Select(ReadRings).ToList(),
			_ => [],
		};
		return new SurfaceShape(polygons);
	}

	/// <summary>
	/// Reads the points of a point or multi-point geometry; any other geometry has no points.
	/// </summary>
	/// <param name="feature">The feature.</param>
	/// <returns>The points in Web Mercator.</returns>
	internal static IReadOnlyList<MercatorPoint> ReadPoints(JsonElement feature)
	{
		var coordinates = JsonValues.Property(JsonValues.Property(feature, "geometry"), "coordinates");
		return GeometryType(feature) switch
		{
			"Point" => [ReadPoint(coordinates)],
			"MultiPoint" => coordinates.EnumerateArray().Select(ReadPoint).ToList(),
			_ => [],
		};
	}

	/// <summary>
	/// Tells whether a geometry is a point or a multi-point.
	/// </summary>
	/// <param name="feature">The feature.</param>
	/// <returns><see langword="true"/> for a point-shaped geometry.</returns>
	internal static bool IsPointShaped(JsonElement feature) => GeometryType(feature) is "Point" or "MultiPoint";
	#endregion

	#region Private Helpers
	private static bool HasGeometry(JsonElement feature)
	{
		var geometry = JsonValues.Property(feature, "geometry");
		return geometry.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
	}

	private static List<IReadOnlyList<MercatorPoint>> ReadRings(JsonElement rings)
	{
		return rings.EnumerateArray().Select(ring => (IReadOnlyList<MercatorPoint>)ring.EnumerateArray().Select(ReadPoint).ToList()).ToList();
	}

	private static MercatorPoint ReadPoint(JsonElement position)
	{
		if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() < 2)
		{
			throw new FormatException("A position needs at least two numbers.");
		}

		return new MercatorPoint(position[0].GetDouble(), position[1].GetDouble());
	}
	#endregion
}
