using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Shared.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Zones;

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
	/// <param name="json">Response body of the zones request.</param>
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
	/// <summary>
	/// Determines whether the properties describe a street where one can park.
	/// </summary>
	/// <param name="properties">The properties of the feature.</param>
	/// <returns><see langword="true"/> when the properties describe a street where one can park; otherwise <see langword="false"/>.</returns>
	private static bool IsParkable(System.Text.Json.JsonElement properties)
	{
		return !JsonValues.IsTruthy(properties, "disabled_on") && SurfaceFilter.IsParkable(JsonValues.Text(properties, "pn_name_fr"));
	}

	/// <summary>
	/// Converts a feature into a zone surface.
	/// </summary>
	/// <param name="feature">The GeoJSON feature object.</param>
	/// <returns>The zone surface.</returns>
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
