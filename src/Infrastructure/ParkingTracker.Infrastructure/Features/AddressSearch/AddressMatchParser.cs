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
/// Purpose     : Turns the Nominatim search answer (a JSON array whose positions are written as text) into address matches, each
///              with a short label and a locality built from its address details. An entry without a readable position is left out.
/// </summary>
internal static class AddressMatchParser
{
	#region Constants
	/// <summary>
	/// The name of this data in failure messages.
	/// </summary>
	internal const string What = "address search";
	#endregion

	#region Static Instances
	/// <summary>
	/// The address details that name the municipality, from the most to the least specific; Nominatim picks one by the size of the place.
	/// </summary>
	private static readonly string[] LocalityNames = ["city", "town", "village", "municipality"];
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
		if (latitude is null || longitude is null)
		{
			return null;
		}

		var displayName = JsonValues.Text(entry, "display_name");
		var address = JsonValues.Property(entry, "address");
		return new AddressMatch(new GeoPoint(latitude.Value, longitude.Value), displayName, LabelOf(entry, address, displayName), LocalityOf(address));
	}

	/// <summary>
	/// Builds the first line of a match: road and house number, else the name of the place, else the first part of the full name.
	/// </summary>
	/// <param name="entry">The JSON object of one result.</param>
	/// <param name="address">The address details of the result.</param>
	/// <param name="displayName">The full name of the result.</param>
	/// <returns>The label, or <see langword="null"/> when the service gave no name at all.</returns>
	private static string? LabelOf(JsonElement entry, JsonElement address, string? displayName)
	{
		var road = JsonValues.Present(address, "road") ?? JsonValues.Present(address, "pedestrian");
		if (road is not null)
		{
			var houseNumber = JsonValues.Present(address, "house_number");
			return houseNumber is null ? road : $"{road} {houseNumber}";
		}

		return JsonValues.Present(entry, "name") ?? FirstPart(displayName);
	}

	/// <summary>
	/// Builds the second line of a match: the postcode and the municipality.
	/// </summary>
	/// <param name="address">The address details of the result.</param>
	/// <returns>The locality, or <see langword="null"/> when the details hold neither.</returns>
	private static string? LocalityOf(JsonElement address)
	{
		var postcode = JsonValues.Present(address, "postcode");
		var city = LocalityNames.Select(name => JsonValues.Present(address, name)).FirstOrDefault(value => value is not null);
		var locality = string.Join(' ', new[] { postcode, city }.Where(part => part is not null));
		return locality.Length == 0 ? null : locality;
	}

	/// <summary>
	/// Takes the part of a full name before its first comma.
	/// </summary>
	/// <param name="displayName">The full name, or <see langword="null"/>.</param>
	/// <returns>The first part, or <see langword="null"/> when there is no name.</returns>
	private static string? FirstPart(string? displayName)
	{
		var first = displayName?.Split(',')[0].Trim();
		return string.IsNullOrEmpty(first) ? null : first;
	}
	#endregion
}
