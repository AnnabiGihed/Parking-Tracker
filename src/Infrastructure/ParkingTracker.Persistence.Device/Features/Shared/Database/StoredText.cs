using System.Globalization;
using System.Text.Json;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Persistence.Device.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The small text formats of the device database that more than one repository uses: a coordinate pair (before it is
///              encrypted), a JSON list of codes and a day. They are invariant of the culture of the phone, so that a database
///              written under one language setting reads under another.
/// </summary>
internal static class StoredText
{
	#region Constants
	/// <summary>
	/// The format of a day, as the card dates of the first version are written.
	/// </summary>
	private const string DayFormat = "yyyy-MM-dd";

	/// <summary>
	/// What separates latitude and longitude.
	/// </summary>
	private const char CoordinateSeparator = ';';
	#endregion

	#region Internal Methods
	/// <summary>
	/// Writes a point as <c>latitude;longitude</c> with the shortest text that reads back to the same numbers.
	/// </summary>
	/// <param name="point">The point.</param>
	/// <returns>The text.</returns>
	internal static string FormatPoint(GeoPoint point)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{point.Latitude:R}{CoordinateSeparator}{point.Longitude:R}");
	}

	/// <summary>
	/// Reads a point written by <see cref="FormatPoint"/>.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <param name="point">The point when the text is valid.</param>
	/// <returns><see langword="true"/> when the text holds two numbers.</returns>
	internal static bool TryParsePoint(string text, out GeoPoint point)
	{
		point = default;
		var parts = text.Split(CoordinateSeparator);
		if (parts.Length != 2
			|| !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
			|| !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
		{
			return false;
		}

		point = new GeoPoint(latitude, longitude);
		return true;
	}

	/// <summary>
	/// Writes a list of codes as a JSON array.
	/// </summary>
	/// <param name="codes">The codes.</param>
	/// <returns>The JSON text.</returns>
	internal static string FormatList(IReadOnlyList<string> codes) => JsonSerializer.Serialize(codes);

	/// <summary>
	/// Reads a JSON array of codes.
	/// </summary>
	/// <param name="json">The JSON text.</param>
	/// <param name="codes">The codes when the text is valid.</param>
	/// <returns><see langword="true"/> when the text is an array of texts.</returns>
	internal static bool TryParseList(string json, out string[] codes)
	{
		codes = [];
		try
		{
			codes = JsonSerializer.Deserialize<string[]>(json) ?? [];
			return codes.All(code => code is not null);
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>
	/// Writes a day, or nothing for a missing day.
	/// </summary>
	/// <param name="day">The day, or <see langword="null"/>.</param>
	/// <returns>The text, or <see langword="null"/>.</returns>
	internal static string? FormatDay(DateOnly? day) => day?.ToString(DayFormat, CultureInfo.InvariantCulture);

	/// <summary>
	/// Reads a day written by <see cref="FormatDay"/>.
	/// </summary>
	/// <param name="text">The text, or <see langword="null"/> for a missing day.</param>
	/// <param name="day">The day, or <see langword="null"/> when the text is missing.</param>
	/// <returns><see langword="true"/> when the text is missing or a day.</returns>
	internal static bool TryParseDay(string? text, out DateOnly? day)
	{
		day = null;
		if (text is null)
		{
			return true;
		}

		if (!DateOnly.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
		{
			return false;
		}

		day = parsed;
		return true;
	}
	#endregion
}
