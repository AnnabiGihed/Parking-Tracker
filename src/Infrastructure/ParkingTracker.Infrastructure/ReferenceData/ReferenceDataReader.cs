using System.Text.Json;
using ParkingTracker.Domain.ReferenceData;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.ReferenceData;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the JSON text of the reference data files into domain records, reporting malformed
///              content as a <c>ReferenceData.InvalidFormat</c> failure instead of throwing.
/// </summary>
internal static class ReferenceDataReader
{
	#region Constants
	/// <summary>
	/// Failure code for a resource that is not part of the assembly.
	/// </summary>
	internal const string MissingCode = "ReferenceData.ResourceMissing";

	/// <summary>
	/// Failure code for a resource whose content does not have the expected shape.
	/// </summary>
	internal const string InvalidCode = "ReferenceData.InvalidFormat";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the municipalities file: a JSON array of municipality objects.
	/// </summary>
	/// <param name="json">The file content.</param>
	/// <returns>The municipalities, or an <c>InvalidFormat</c> failure.</returns>
	internal static Result<IReadOnlyList<Municipality>> ReadMunicipalities(string json)
	{
		return Read<IReadOnlyList<Municipality>>(json, "municipalities", root => root.EnumerateArray().Select(ToMunicipality).ToList());
	}

	/// <summary>
	/// Reads a sectors file: a JSON object keyed by sector identifier. Rows of a sector's <c>streets</c> read an empty
	/// cell as the whole side; rows of a tolerance agreement read it as not covered
	/// (<see cref="SectorStreet.EmptyMeansWholeSide"/>). The sector-level <c>"kind": "streets"</c> property of
	/// <c>city-sectors.json</c> is deliberately ignored: it only mirrors the web copy and carries no information.
	/// </summary>
	/// <param name="json">The file content.</param>
	/// <param name="resourceName">The file name, used in the failure message.</param>
	/// <returns>The sectors in file order, or an <c>InvalidFormat</c> failure.</returns>
	internal static Result<IReadOnlyList<SectorDefinition>> ReadSectors(string json, string resourceName)
	{
		return Read<IReadOnlyList<SectorDefinition>>(json, resourceName, root => root.EnumerateObject().Select(p => ToSector(p.Value)).ToList());
	}

	/// <summary>
	/// Builds the failure for a resource that cannot be found.
	/// </summary>
	/// <param name="resourceName">The missing resource.</param>
	/// <returns>The error.</returns>
	internal static Error Missing(string resourceName) => new(MissingCode, $"Reference data resource '{resourceName}' is not embedded in the assembly.");
	#endregion

	#region Private Helpers
	private static Result<T> Read<T>(string json, string resourceName, Func<JsonElement, T> convert)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			return Result.Success(convert(document.RootElement));
		}
		catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
		{
			return Result.Failure<T>(new Error(InvalidCode, $"Reference data resource '{resourceName}' is invalid: {exception.Message}"));
		}
	}

	private static Municipality ToMunicipality(JsonElement element)
	{
		var kind = Text(element, "kind");
		if (!IsNamedKind(kind) || !Enum.TryParse<MunicipalityKind>(kind, ignoreCase: true, out var parsedKind) || !Enum.IsDefined(parsedKind))
		{
			throw new FormatException($"Unknown municipality kind '{kind}'.");
		}

		return new Municipality(
			Text(element, "id"),
			Text(element, "name"),
			Text(element, "nameNl"),
			Text(element, "mu"),
			parsedKind,
			Texts(element, "prefixes"),
			Texts(element, "lim"),
			OptionalText(element, "note"));
	}

	private static bool IsNamedKind(string kind)
	{
		return kind.Length > 0 && char.IsLetter(kind[0]);
	}

	private static SectorDefinition ToSector(JsonElement element)
	{
		return new SectorDefinition(
			Text(element, "id"),
			Text(element, "name"),
			Text(element, "nameNl"),
			OptionalText(element, "municipality"),
			ToArea(element),
			Items(element, "sources", ToFingerprint),
			Items(element, "streets", row => ToStreet(row, emptyMeansWholeSide: true)),
			Items(element, "tolerances", ToTolerance));
	}

	private static SectorArea? ToArea(JsonElement sector)
	{
		if (!sector.TryGetProperty("area", out var area))
		{
			return null;
		}

		var bounds = area.EnumerateArray().Select(value => value.GetDouble()).ToArray();
		return bounds.Length == 4
			? new SectorArea(bounds[0], bounds[1], bounds[2], bounds[3])
			: throw new FormatException("A sector area needs four bounds: south, west, north, east.");
	}

	private static SourceFingerprint ToFingerprint(JsonElement element)
	{
		return new SourceFingerprint(
			Text(element, "url"),
			OptionalText(element, "etag"),
			element.GetProperty("length").GetInt64(),
			OptionalText(element, "lastModified"),
			Text(element, "sha256"));
	}

	private static ToleranceAgreement ToTolerance(JsonElement element)
	{
		return new ToleranceAgreement(
			Text(element, "code"),
			Text(element, "label"),
			Text(element, "commune"),
			Items(element, "streets", row => ToStreet(row, emptyMeansWholeSide: false)));
	}

	private static SectorStreet ToStreet(JsonElement row, bool emptyMeansWholeSide)
	{
		var cells = row.EnumerateArray().Select(cell => cell.GetString() ?? throw new FormatException("A street cell is null.")).ToArray();
		if (cells.Length is not (4 or 5))
		{
			throw new FormatException($"A street row has {cells.Length} cells; expected 4 or 5.");
		}

		var postcode = cells.Length == 5 ? cells[4] : null;
		return new SectorStreet(cells[0], cells[1], cells[2], cells[3], postcode, emptyMeansWholeSide);
	}

	private static string Text(JsonElement element, string name)
	{
		return element.GetProperty(name).GetString() ?? throw new FormatException($"Property '{name}' is null.");
	}

	private static string? OptionalText(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
	}

	private static List<string> Texts(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out var values)
			? values.EnumerateArray().Select(value => value.GetString() ?? throw new FormatException($"An item of '{name}' is null.")).ToList()
			: [];
	}

	private static List<T> Items<T>(JsonElement element, string name, Func<JsonElement, T> convert)
	{
		return element.TryGetProperty(name, out var values) ? values.EnumerateArray().Select(convert).ToList() : [];
	}
	#endregion
}
