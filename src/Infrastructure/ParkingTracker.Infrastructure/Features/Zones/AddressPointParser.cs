using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Shared.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the UrbIS <c>urbadm_adpt</c> answer into address points. Features that are not points or whose house
///              number cannot be read are left out.
/// </summary>
internal static class AddressPointParser
{
	#region Constants
	/// <summary>
	/// The name of this data in failure messages.
	/// </summary>
	internal const string What = "UrbIS";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the answer of the address points request.
	/// </summary>
	/// <param name="json">Response body of the address points request.</param>
	/// <returns>The address points, or an <c>Urbis.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyList<AddressPoint>> Parse(string json)
	{
		return GeoJson.ReadFeatures<IReadOnlyList<AddressPoint>>(json, SourceKind.Urbis, What, FeatureFilter.All, features => features
			.Select(ToAddressPoint)
			.OfType<AddressPoint>()
			.ToList());
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Converts a feature into an address point.
	/// </summary>
	/// <param name="feature">The GeoJSON feature object.</param>
	/// <returns>The address point, or <see langword="null"/> when the feature is not usable.</returns>
	private static AddressPoint? ToAddressPoint(System.Text.Json.JsonElement feature)
	{
		var text = JsonValues.TextOrNumber(GeoJson.Properties(feature), "adrn");
		var house = HouseNumber.Parse(text);
		if (house is null || !string.Equals(GeoJson.GeometryType(feature), "Point", StringComparison.Ordinal))
		{
			return null;
		}

		return new AddressPoint(text!.Trim(), house, GeoJson.ReadPoints(feature)[0]);
	}
	#endregion
}
