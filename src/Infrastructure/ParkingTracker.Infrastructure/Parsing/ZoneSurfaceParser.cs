using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Zones;
using ParkingTracker.Infrastructure.Sources;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Parsing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the parking.brussels <c>regulation_zones</c> answer into street surfaces. Features without a geometry,
///              disabled surfaces and tunnels, viaducts, trémies and bridges are left out: nobody parks there, and their
///              surface would hide the street above or below (for example Tunnel Bailli under Avenue Louise).
/// </summary>
internal static class ZoneSurfaceParser
{
	#region Constants
	/// <summary>
	/// The name of this data in failure messages.
	/// </summary>
	internal const string What = "parking.brussels";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the answer of the zones request.
	/// </summary>
	/// <param name="json">The answer.</param>
	/// <returns>The surfaces, or a <c>ParkingBrussels.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyList<ZoneSurface>> Parse(string json)
	{
		return GeoJson.ReadFeatures<IReadOnlyList<ZoneSurface>>(json, SourceKind.ParkingBrussels, What, FeatureFilter.WithGeometry, features => features
			.Where(feature => IsParkable(GeoJson.Properties(feature)))
			.Select(ToSurface)
			.ToList());
	}
	#endregion

	#region Private Helpers
	private static bool IsParkable(System.Text.Json.JsonElement properties)
	{
		return !JsonValues.IsTruthy(properties, "disabled_on") && SurfaceFilter.IsParkable(JsonValues.Text(properties, "pn_name_fr"));
	}

	private static ZoneSurface ToSurface(System.Text.Json.JsonElement feature)
	{
		var properties = GeoJson.Properties(feature);
		var code = JsonValues.Text(properties, "ZoneId1");
		code = string.IsNullOrEmpty(code) ? JsonValues.Text(properties, "zoneid1") : code;
		return new ZoneSurface(
			JsonValues.TextOrNumber(properties, "id"),
			JsonValues.Text(properties, "pn_name_fr") ?? string.Empty,
			JsonValues.Text(properties, "pn_name_du") ?? string.Empty,
			JsonValues.Text(properties, "mu_name_fr") ?? string.Empty,
			(int?)JsonValues.Number(properties, "typreg"),
			string.IsNullOrEmpty(code) ? null : code,
			GeoJson.ReadShape(feature));
	}
	#endregion
}
