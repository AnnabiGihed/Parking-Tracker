using System.Globalization;
using System.Text.Json;

namespace ParkingTracker.Infrastructure.Features.Shared.Parsing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads values of the live JSON answers the way the first version of the app did: a missing property, a null and
///              a value of another type all read as "not there", and a number may come as text.
/// </summary>
internal static class JsonValues
{
	#region Internal Methods
	/// <summary>
	/// Gets a child property of an object.
	/// </summary>
	/// <param name="element">The object; any other kind of value, or an undefined one, has no properties.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>The property value, or an undefined element when there is none.</returns>
	internal static JsonElement Property(JsonElement element, string name)
	{
		return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
	}

	/// <summary>
	/// Reads a text property.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>The text, or <see langword="null"/> when the property is missing, null or not text.</returns>
	internal static string? Text(JsonElement element, string name)
	{
		var value = Property(element, name);
		return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
	}

	/// <summary>
	/// Reads a property that is text or a number as text, for identifiers.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>The text, or <see langword="null"/> when the property is missing, null or neither.</returns>
	internal static string? TextOrNumber(JsonElement element, string name)
	{
		var value = Property(element, name);
		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => value.GetRawText(),
			_ => null,
		};
	}

	/// <summary>
	/// Reads a number property; a text that holds a number counts as that number.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>The number, or <see langword="null"/> when the property is missing, empty or not a number.</returns>
	internal static double? Number(JsonElement element, string name)
	{
		var value = Property(element, name);
		return value.ValueKind switch
		{
			JsonValueKind.Number => value.GetDouble(),
			JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => null,
		};
	}

	/// <summary>
	/// Reads an amount of money or another decimal property; a text that holds a number counts as that number.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>The amount, or <see langword="null"/> when the property is missing, empty or not a number.</returns>
	internal static decimal? Decimal(JsonElement element, string name)
	{
		var value = Property(element, name);
		var text = value.ValueKind switch
		{
			JsonValueKind.Number => value.GetRawText(),
			JsonValueKind.String => value.GetString(),
			_ => null,
		};
		return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
	}

	/// <summary>
	/// Tells whether a property is a JSON number equal to a value. A text never equals a number here, as with strict
	/// equality in the first version of the app.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <param name="expected">The number to compare with.</param>
	/// <returns><see langword="true"/> when the property is a number and equals <paramref name="expected"/>.</returns>
	internal static bool IsNumber(JsonElement element, string name, int expected)
	{
		var value = Property(element, name);
		return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number == expected;
	}

	/// <summary>
	/// Tells whether a property holds a value that counts as true: not missing, null, false, 0 or empty text.
	/// </summary>
	/// <param name="element">JSON object to read from.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns><see langword="true"/> for a truthy value.</returns>
	internal static bool IsTruthy(JsonElement element, string name)
	{
		var value = Property(element, name);
		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString()!.Length > 0,
			JsonValueKind.Number => !value.TryGetInt32(out var number) || number != 0,
			JsonValueKind.True or JsonValueKind.Array or JsonValueKind.Object => true,
			_ => false,
		};
	}
	#endregion
}
