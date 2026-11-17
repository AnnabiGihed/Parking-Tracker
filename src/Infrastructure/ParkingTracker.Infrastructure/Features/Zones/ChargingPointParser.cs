using System.Text.Json;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Shared.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the Brussels Mobility <c>bm_equipment:evse</c> answer into charging points. A feature without a point is
///              left out; a value that is zero or empty counts as unknown.
/// </summary>
internal static class ChargingPointParser
{
	#region Constants
	/// <summary>
	/// The name of this data in failure messages.
	/// </summary>
	internal const string What = "charging points";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the answer of the charging points request.
	/// </summary>
	/// <param name="json">Response body of the charging points request.</param>
	/// <returns>The charging points, or a <c>Mobility.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyList<ChargingPoint>> Parse(string json)
	{
		return GeoJson.ReadFeatures<IReadOnlyList<ChargingPoint>>(json, SourceKind.Mobility, What, FeatureFilter.WithGeometry, features => features
			.Select(ToChargingPoint)
			.OfType<ChargingPoint>()
			.ToList());
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Converts a feature into a charging point.
	/// </summary>
	/// <param name="feature">The GeoJSON feature object.</param>
	/// <returns>The charging point, or <see langword="null"/> when the feature has no point.</returns>
	private static ChargingPoint? ToChargingPoint(JsonElement feature)
	{
		var properties = GeoJson.Properties(feature);
		var points = GeoJson.ReadPoints(feature);
		return points.Count == 0
			? null
			: new ChargingPoint(
				WebMercator.ToGeo(points[0]),
				JsonValues.Present(properties, "operator_name"),
				JsonValues.Present(properties, "charging_points"),
				JsonValues.Present(properties, "power"),
				JsonValues.Present(properties, "connector_standard"),
				JsonValues.Present(properties, "connector_type"));
	}
	#endregion
}
