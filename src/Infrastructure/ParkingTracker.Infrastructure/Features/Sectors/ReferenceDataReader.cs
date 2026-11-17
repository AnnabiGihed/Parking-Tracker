using System.Text.Json;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Sectors;

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
	/// <returns>Error reporting the missing resource.</returns>
	internal static Error Missing(string resourceName) => new(MissingCode, $"Reference data resource '{resourceName}' is not embedded in the assembly.");
	#endregion

	#region Private Helpers
	/// <summary>
	/// Parses the JSON of a resource and converts its root.
	/// </summary>
	/// <typeparam name="T">The type of the converted value.</typeparam>
	/// <param name="json">The JSON text.</param>
	/// <param name="resourceName">The name of the resource.</param>
	/// <param name="convert">The conversion of each element.</param>
	/// <returns>The converted content, or the failure when the JSON is invalid.</returns>
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

	/// <summary>
	/// Converts a municipality element.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <returns>Municipality read from the JSON object.</returns>
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

	/// <summary>
	/// Determines whether the kind text names a kind of municipality.
	/// </summary>
	/// <param name="kind">Municipality kind text from the file.</param>
	/// <returns><see langword="true"/> when the kind text names a kind of municipality; otherwise <see langword="false"/>.</returns>
	private static bool IsNamedKind(string kind)
	{
		return kind.Length > 0 && char.IsLetter(kind[0]);
	}

	/// <summary>
	/// Converts a sector element.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <returns>The sector definition.</returns>
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

	/// <summary>
	/// Converts the area of a sector element.
	/// </summary>
	/// <param name="sector">JSON object of the sector.</param>
	/// <returns>The area, or <see langword="null"/> when the sector has none.</returns>
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

	/// <summary>
	/// Converts a source element into the fingerprint of an official document.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <returns>Source fingerprint read from the JSON object.</returns>
	private static SourceFingerprint ToFingerprint(JsonElement element)
	{
		return new SourceFingerprint(
			Text(element, "url"),
			OptionalText(element, "etag"),
			element.GetProperty("length").GetInt64(),
			OptionalText(element, "lastModified"),
			Text(element, "sha256"));
	}

	/// <summary>
	/// Converts a tolerance agreement element.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <returns>The tolerance agreement.</returns>
	private static ToleranceAgreement ToTolerance(JsonElement element)
	{
		return new ToleranceAgreement(
			Text(element, "code"),
			Text(element, "label"),
			Text(element, "commune"),
			Items(element, "streets", row => ToStreet(row, emptyMeansWholeSide: false)));
	}

	/// <summary>
	/// Converts a street row element.
	/// </summary>
	/// <param name="row">JSON object of one street row.</param>
	/// <param name="emptyMeansWholeSide">The empty means whole side.</param>
	/// <returns>The street row.</returns>
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

	/// <summary>
	/// Reads a required text property.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>Required text value of the property.</returns>
	private static string Text(JsonElement element, string name)
	{
		return element.GetProperty(name).GetString() ?? throw new FormatException($"Property '{name}' is null.");
	}

	/// <summary>
	/// Reads an optional text property.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>The text, or <see langword="null"/> when the property is absent or null.</returns>
	private static string? OptionalText(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
	}

	/// <summary>
	/// Reads an array of texts.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>The texts, empty when the property is absent.</returns>
	private static List<string> Texts(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out var values)
			? values.EnumerateArray().Select(value => value.GetString() ?? throw new FormatException($"An item of '{name}' is null.")).ToList()
			: [];
	}

	/// <summary>
	/// Reads an array and converts each element.
	/// </summary>
	/// <typeparam name="T">The type of the converted value.</typeparam>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <param name="convert">The conversion of each element.</param>
	/// <returns>The converted elements, empty when the property is absent.</returns>
	private static List<T> Items<T>(JsonElement element, string name, Func<JsonElement, T> convert)
	{
		return element.TryGetProperty(name, out var values) ? values.EnumerateArray().Select(convert).ToList() : [];
	}
	#endregion
}
