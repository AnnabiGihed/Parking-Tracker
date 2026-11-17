using System.Text.Json;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Shared.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.AddressSearch;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the Nominatim search answer (a JSON array whose positions are written as text) into address matches. An
///              entry without a readable position is left out.
/// </summary>
internal static class AddressMatchParser
{
	#region Constants
	/// <summary>
	/// The name of this data in failure messages.
	/// </summary>
	internal const string What = "address search";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the answer of the address search request.
	/// </summary>
	/// <param name="json">Response body of the search request.</param>
	/// <returns>The matches in the order the service gave them, or a <c>Nominatim.BadResponse</c> failure when the answer is not an array.</returns>
	internal static Result<IReadOnlyList<AddressMatch>> Parse(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			if (document.RootElement.ValueKind != JsonValueKind.Array)
			{
				return Result.Failure<IReadOnlyList<AddressMatch>>(SourceFailures.BadResponse(SourceKind.Nominatim, What));
			}

			return Result.Success<IReadOnlyList<AddressMatch>>([.. document.RootElement.EnumerateArray().Select(ToMatch).OfType<AddressMatch>()]);
		}
		catch (JsonException)
		{
			return Result.Failure<IReadOnlyList<AddressMatch>>(SourceFailures.BadResponse(SourceKind.Nominatim, What));
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Converts one entry of the answer into a match.
	/// </summary>
	/// <param name="entry">The JSON object of one result.</param>
	/// <returns>The match, or <see langword="null"/> when its position is missing.</returns>
	private static AddressMatch? ToMatch(JsonElement entry)
	{
		var latitude = JsonValues.Number(entry, "lat");
		var longitude = JsonValues.Number(entry, "lon");
		return latitude is null || longitude is null ? null : new AddressMatch(new GeoPoint(latitude.Value, longitude.Value), JsonValues.Text(entry, "display_name"));
	}
	#endregion
}
