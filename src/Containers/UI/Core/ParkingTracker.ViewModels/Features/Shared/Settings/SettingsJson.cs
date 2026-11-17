using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads and writes the saved values as the first version of the app (<c>app.js</c>) wrote them to <c>localStorage</c>, so
///              that a value copied from the old storage (ADR-0004) is read unchanged and a value written here can still be read by
///              the old code. The readers forgive what <c>JSON.parse</c> plus the old defaults forgave: unreadable text and a missing
///              field give "nothing saved" or the default, never an exception; a card without a municipality is a City of Brussels
///              card. Property names, their order and the date format (<c>yyyy-MM-dd</c>) are the contract; do not change them.
/// </summary>
public static class SettingsJson
{
	#region Constants
	/// <summary>
	/// The key of the residents' card (encrypted storage).
	/// </summary>
	public const string CardKey = "pt.card";

	/// <summary>
	/// The key of the parked car.
	/// </summary>
	public const string CarKey = "pt.car";

	/// <summary>
	/// The key of the map layer switches.
	/// </summary>
	public const string LayersKey = "pt.layers";

	/// <summary>
	/// The key of the moment until which the location banner is postponed.
	/// </summary>
	public const string PermissionSnoozeKey = "pt.permSnooze";

	/// <summary>
	/// The key of the flag that the welcome dialog was seen; new in the .NET version, so the old storage has no such value.
	/// </summary>
	public const string WelcomeSeenKey = "pt.welcomeSeen";

	/// <summary>
	/// The municipality of a card saved before cards could name one.
	/// </summary>
	public const string DefaultMunicipality = "bruxelles";

	/// <summary>
	/// The largest moment, in milliseconds since 1970, that a <see cref="DateTimeOffset"/> can hold (31 December 9999).
	/// </summary>
	private const double MaxUnixMilliseconds = 253402300799999;

	/// <summary>
	/// The format of the card dates, as a date input of a browser writes them.
	/// </summary>
	private const string DateFormat = "yyyy-MM-dd";
	#endregion

	#region Card
	/// <summary>
	/// Reads the <c>pt.card</c> value.
	/// </summary>
	/// <param name="json">The saved text, or <see langword="null"/> when nothing is saved.</param>
	/// <returns>The card, or <see langword="null"/> when the text is missing or is not a JSON object.</returns>
	public static ParkingCard? ParseCard(string? json)
	{
		using var document = ParseObject(json);
		if (document is null)
		{
			return null;
		}

		var root = document.RootElement;
		var municipality = ReadString(root, "municipality");
		return new ParkingCard(
			string.IsNullOrWhiteSpace(municipality) ? DefaultMunicipality : municipality,
			NullIfEmpty(ReadString(root, "sector")),
			new CardPeriod(ReadDate(root, "from"), ReadDate(root, "to")),
			ReadStrings(root, "tolerances"),
			ReadStrings(root, "mailles"),
			ReadStrings(root, "lims"),
			NullIfEmpty(ReadString(root, "number")),
			NullIfEmpty(ReadString(root, "plate")));
	}

	/// <summary>
	/// Writes a card as the <c>pt.card</c> value: <c>number</c>, <c>plate</c>, <c>municipality</c>, <c>sector</c>, <c>from</c>,
	/// <c>to</c>, <c>tolerances</c>, <c>mailles</c> and <c>lims</c>; an empty text where the card has none, <c>null</c> for no sector.
	/// </summary>
	/// <param name="card">The card to write.</param>
	/// <returns>The JSON text.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="card"/> is <see langword="null"/>.</exception>
	public static string SerializeCard(ParkingCard card)
	{
		ArgumentNullException.ThrowIfNull(card);
		return Write(writer =>
		{
			writer.WriteStartObject();
			writer.WriteString("number", card.Number ?? string.Empty);
			writer.WriteString("plate", card.Plate ?? string.Empty);
			writer.WriteString("municipality", card.Municipality);
			if (card.Sector is null)
			{
				writer.WriteNull("sector");
			}
			else
			{
				writer.WriteString("sector", card.Sector);
			}

			writer.WriteString("from", FormatDate(card.Period.ValidFrom));
			writer.WriteString("to", FormatDate(card.Period.ValidTo));
			WriteStrings(writer, "tolerances", card.Tolerances);
			WriteStrings(writer, "mailles", card.Mailles);
			WriteStrings(writer, "lims", card.Lims);
			writer.WriteEndObject();
		});
	}
	#endregion

	#region Car
	/// <summary>
	/// Reads the <c>pt.car</c> value: <c>lat</c>, <c>lon</c>, <c>t</c> (milliseconds since 1970), <c>status</c> and <c>where</c>.
	/// </summary>
	/// <param name="json">The saved text, or <see langword="null"/> when nothing is saved.</param>
	/// <returns>The car, or <see langword="null"/> when the text is missing or has no position.</returns>
	public static ParkedCar? ParseCar(string? json)
	{
		using var document = ParseObject(json);
		if (document is null)
		{
			return null;
		}

		var root = document.RootElement;
		if (ReadNumber(root, "lat") is not { } latitude || ReadNumber(root, "lon") is not { } longitude)
		{
			return null;
		}

		return new ParkedCar(
			new GeoPoint(latitude, longitude),
			FromUnixMilliseconds(ReadNumber(root, "t") ?? 0),
			ReadString(root, "status") ?? string.Empty,
			ReadString(root, "where") ?? string.Empty);
	}

	/// <summary>
	/// Writes a car as the <c>pt.car</c> value.
	/// </summary>
	/// <param name="car">The car to write.</param>
	/// <returns>The JSON text.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="car"/> is <see langword="null"/>.</exception>
	public static string SerializeCar(ParkedCar car)
	{
		ArgumentNullException.ThrowIfNull(car);
		return Write(writer =>
		{
			writer.WriteStartObject();
			writer.WriteNumber("lat", car.Point.Latitude);
			writer.WriteNumber("lon", car.Point.Longitude);
			writer.WriteNumber("t", car.Time.ToUnixTimeMilliseconds());
			writer.WriteString("status", car.Status);
			writer.WriteString("where", car.Where);
			writer.WriteEndObject();
		});
	}
	#endregion

	#region Layers
	/// <summary>
	/// Reads the <c>pt.layers</c> value: <c>streets</c>, <c>spaces</c>, <c>works</c>, <c>carparks</c> and <c>chargers</c> as booleans.
	/// </summary>
	/// <param name="json">The saved text, or <see langword="null"/> when nothing is saved.</param>
	/// <returns>The switches; a switch that is missing or not a boolean keeps its default.</returns>
	public static LayerSettings ParseLayers(string? json)
	{
		var defaults = new LayerSettings();
		using var document = ParseObject(json);
		if (document is null)
		{
			return defaults;
		}

		var root = document.RootElement;
		return new LayerSettings(
			ReadBoolean(root, "streets") ?? defaults.Streets,
			ReadBoolean(root, "spaces") ?? defaults.Spaces,
			ReadBoolean(root, "works") ?? defaults.Works,
			ReadBoolean(root, "carparks") ?? defaults.CarParks,
			ReadBoolean(root, "chargers") ?? defaults.Chargers);
	}

	/// <summary>
	/// Writes the switches as the <c>pt.layers</c> value.
	/// </summary>
	/// <param name="layers">The switches to write.</param>
	/// <returns>The JSON text.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="layers"/> is <see langword="null"/>.</exception>
	public static string SerializeLayers(LayerSettings layers)
	{
		ArgumentNullException.ThrowIfNull(layers);
		return Write(writer =>
		{
			writer.WriteStartObject();
			writer.WriteBoolean("streets", layers.Streets);
			writer.WriteBoolean("spaces", layers.Spaces);
			writer.WriteBoolean("works", layers.Works);
			writer.WriteBoolean("carparks", layers.CarParks);
			writer.WriteBoolean("chargers", layers.Chargers);
			writer.WriteEndObject();
		});
	}
	#endregion

	#region Permission Snooze
	/// <summary>
	/// Reads the <c>pt.permSnooze</c> value: a number of milliseconds since 1970.
	/// </summary>
	/// <param name="json">The saved text, or <see langword="null"/> when nothing is saved.</param>
	/// <returns>The moment, or <see langword="null"/> when the text is missing or is not a number.</returns>
	public static DateTimeOffset? ParsePermissionSnooze(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;
			return root.ValueKind == JsonValueKind.Number && root.TryGetDouble(out var milliseconds)
				? FromUnixMilliseconds(milliseconds)
				: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>
	/// Writes the moment as the <c>pt.permSnooze</c> value.
	/// </summary>
	/// <param name="until">The moment the banner may show again.</param>
	/// <returns>The number of milliseconds since 1970 as text.</returns>
	public static string SerializePermissionSnooze(DateTimeOffset until)
	{
		return until.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Parses text that must be a JSON object.
	/// </summary>
	/// <param name="json">The text, or <see langword="null"/>.</param>
	/// <returns>The document, or <see langword="null"/> when the text is missing, unreadable or not an object. The caller disposes it.</returns>
	private static JsonDocument? ParseObject(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json);
		}
		catch (JsonException)
		{
			return null;
		}

		if (document.RootElement.ValueKind == JsonValueKind.Object)
		{
			return document;
		}

		document.Dispose();
		return null;
	}

	/// <summary>
	/// Reads a text property.
	/// </summary>
	/// <param name="element">The object.</param>
	/// <param name="name">The property name.</param>
	/// <returns>The text, or <see langword="null"/> when the property is missing or not text.</returns>
	private static string? ReadString(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
	}

	/// <summary>
	/// Reads a number property.
	/// </summary>
	/// <param name="element">The object.</param>
	/// <param name="name">The property name.</param>
	/// <returns>The number, or <see langword="null"/> when the property is missing or not a number.</returns>
	private static double? ReadNumber(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
			? number
			: null;
	}

	/// <summary>
	/// Reads a boolean property.
	/// </summary>
	/// <param name="element">The object.</param>
	/// <param name="name">The property name.</param>
	/// <returns>The flag, or <see langword="null"/> when the property is missing or not a boolean.</returns>
	private static bool? ReadBoolean(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			_ => null,
		};
	}

	/// <summary>
	/// Reads a date property written as <c>yyyy-MM-dd</c>.
	/// </summary>
	/// <param name="element">The object.</param>
	/// <param name="name">The property name.</param>
	/// <returns>The date, or <see langword="null"/> when the property is missing, empty or not a date.</returns>
	private static DateOnly? ReadDate(JsonElement element, string name)
	{
		return DateOnly.TryParseExact(ReadString(element, name), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
	}

	/// <summary>
	/// Reads an array of texts; anything else in the array is skipped.
	/// </summary>
	/// <param name="element">The object.</param>
	/// <param name="name">The property name.</param>
	/// <returns>The texts; empty when the property is missing or not an array.</returns>
	private static string[] ReadStrings(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		return value.EnumerateArray()
			.Where(item => item.ValueKind == JsonValueKind.String)
			.Select(item => item.GetString()!)
			.ToArray();
	}

	/// <summary>
	/// Converts milliseconds since 1970 to a moment, the way <c>Date.now()</c> counts.
	/// </summary>
	/// <param name="milliseconds">The count; may have a fraction.</param>
	/// <returns>The moment; 1970 when the count is outside what a moment can hold.</returns>
	private static DateTimeOffset FromUnixMilliseconds(double milliseconds)
	{
		return Math.Abs(milliseconds) <= MaxUnixMilliseconds
			? DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds)
			: DateTimeOffset.UnixEpoch;
	}

	/// <summary>
	/// Turns an empty text into <see langword="null"/>.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <returns>The text, or <see langword="null"/> when it is missing or empty.</returns>
	private static string? NullIfEmpty(string? text)
	{
		return string.IsNullOrEmpty(text) ? null : text;
	}

	/// <summary>
	/// Formats a card date; an empty text when there is none, as the date input wrote it.
	/// </summary>
	/// <param name="date">The date, or <see langword="null"/>.</param>
	/// <returns>The date as <c>yyyy-MM-dd</c>, or an empty text.</returns>
	private static string FormatDate(DateOnly? date)
	{
		return date?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;
	}

	/// <summary>
	/// Writes a property that holds an array of texts.
	/// </summary>
	/// <param name="writer">The writer.</param>
	/// <param name="name">The property name.</param>
	/// <param name="values">The texts.</param>
	private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
	{
		writer.WriteStartArray(name);
		foreach (var value in values)
		{
			writer.WriteStringValue(value);
		}

		writer.WriteEndArray();
	}

	/// <summary>
	/// Runs a writer into text. Non-ASCII characters stay as they are, as <c>JSON.stringify</c> leaves them.
	/// </summary>
	/// <param name="write">Writes the document.</param>
	/// <returns>The JSON text.</returns>
	private static string Write(Action<Utf8JsonWriter> write)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
		{
			write(writer);
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}
	#endregion
}
