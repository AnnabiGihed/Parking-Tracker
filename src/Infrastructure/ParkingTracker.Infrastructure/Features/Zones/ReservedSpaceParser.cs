using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Shared.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the parking.brussels <c>on_street</c> answer into individual parking spaces, one per point, each with its
///              reservation type (0 for an ordinary space).
/// </summary>
internal static class ReservedSpaceParser
{
	#region Constants
	/// <summary>
	/// The name of this data in failure messages.
	/// </summary>
	internal const string What = "parking.brussels spaces";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the answer of the spaces request.
	/// </summary>
	/// <param name="json">Response body of the reserved spaces request.</param>
	/// <returns>The spaces, or a <c>ParkingBrussels.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyList<ReservedSpace>> Parse(string json)
	{
		return GeoJson.ReadFeatures<IReadOnlyList<ReservedSpace>>(json, SourceKind.ParkingBrussels, What, FeatureFilter.WithGeometry, features => features
			.SelectMany(feature =>
			{
				var reservation = (int?)JsonValues.Number(GeoJson.Properties(feature), "typres") ?? 0;
				return GeoJson.ReadPoints(feature).Select(point => new ReservedSpace(WebMercator.ToGeo(point), reservation));
			})
			.ToList());
	}
	#endregion
}
