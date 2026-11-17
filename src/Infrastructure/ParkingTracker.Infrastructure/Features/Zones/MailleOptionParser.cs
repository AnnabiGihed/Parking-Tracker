using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Shared.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the parking.brussels answer of the sector list (zone code and names only, no shapes) into the areas the
///              card form offers. An entry without a zone code is left out.
/// </summary>
internal static class MailleOptionParser
{
	#region Constants
	/// <summary>
	/// Short description of the request, used in error messages.
	/// </summary>
	internal const string What = "parking.brussels sector list";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the answer of the sector list request.
	/// </summary>
	/// <param name="json">Response body of the sector list request.</param>
	/// <returns>The areas, or a <c>ParkingBrussels.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyList<MailleOption>> Parse(string json)
	{
		return GeoJson.ReadFeatures<IReadOnlyList<MailleOption>>(json, SourceKind.ParkingBrussels, What, FeatureFilter.All, features => features
			.Select(feature => new MailleOption(JsonValues.Text(GeoJson.Properties(feature), "zonecode") ?? string.Empty, JsonValues.Text(GeoJson.Properties(feature), "namefre")))
			.Where(option => option.ZoneCode.Length > 0)
			.ToList());
	}
	#endregion
}
