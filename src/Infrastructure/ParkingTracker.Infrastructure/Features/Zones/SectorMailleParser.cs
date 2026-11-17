using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Shared.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the parking.brussels <c>permit_secteurs_mailles</c> answer into sector areas.
/// </summary>
internal static class SectorMailleParser
{
	#region Constants
	/// <summary>
	/// The name of this data in failure messages.
	/// </summary>
	internal const string What = "parking.brussels sectors";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the answer of the sector areas request.
	/// </summary>
	/// <param name="json">Response body of the sector mailles request.</param>
	/// <returns>The areas, or a <c>ParkingBrussels.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyList<SectorMaille>> Parse(string json)
	{
		return GeoJson.ReadFeatures<IReadOnlyList<SectorMaille>>(json, SourceKind.ParkingBrussels, What, FeatureFilter.WithGeometry, features => features
			.Select(feature => new SectorMaille(
				JsonValues.Text(GeoJson.Properties(feature), "zonecode") ?? string.Empty,
				JsonValues.Text(GeoJson.Properties(feature), "namefre"),
				GeoJson.ReadShape(feature)))
			.ToList());
	}
	#endregion
}
