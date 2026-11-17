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
/// Purpose     : Turns the Brussels Mobility <c>bm_parking:parkingpublic</c> answer into car parks. A car park the service marks
///              as not active, or that has no point, is left out; a value that is zero or empty counts as unknown, as the first
///              version of the map printed it.
/// </summary>
internal static class CarParkParser
{
	#region Constants
	/// <summary>
	/// The name of this data in failure messages.
	/// </summary>
	internal const string What = "car parks";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the answer of the car parks request.
	/// </summary>
	/// <param name="json">Response body of the car parks request.</param>
	/// <returns>The car parks, or a <c>Mobility.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyList<CarPark>> Parse(string json)
	{
		return GeoJson.ReadFeatures<IReadOnlyList<CarPark>>(json, SourceKind.Mobility, What, FeatureFilter.WithGeometry, features => features
			.Select(ToCarPark)
			.OfType<CarPark>()
			.ToList());
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Converts a feature into a car park.
	/// </summary>
	/// <param name="feature">The GeoJSON feature object.</param>
	/// <returns>The car park, or <see langword="null"/> when it is not active or has no point.</returns>
	private static CarPark? ToCarPark(JsonElement feature)
	{
		var properties = GeoJson.Properties(feature);
		var points = GeoJson.ReadPoints(feature);
		if (JsonValues.Property(properties, "active").ValueKind == JsonValueKind.False || points.Count == 0)
		{
			return null;
		}

		var address = string.Join(' ', new[] { JsonValues.Present(properties, "road_fr"), JsonValues.Present(properties, "housenr") }.Where(part => part is not null));
		var maxHeight = JsonValues.Number(properties, "maxheight") > 0 ? JsonValues.TextOrNumber(properties, "maxheight") : null;
		return new CarPark(
			WebMercator.ToGeo(points[0]),
			JsonValues.Text(properties, "name_fr"),
			address,
			JsonValues.Present(properties, "capacity"),
			JsonValues.Present(properties, "disabledcapacity"),
			JsonValues.Text(properties, "operator_fr"),
			JsonValues.Text(properties, "contact_phone"),
			maxHeight);
	}
	#endregion
}
