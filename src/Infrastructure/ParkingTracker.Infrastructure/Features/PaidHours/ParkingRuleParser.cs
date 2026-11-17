using System.Globalization;
using System.Text.Json;
using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Shared.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.PaidHours;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the Brussels Mobility parking rules answer into the rule in force per zone code. Inactive, disabled and
///              not yet active rules are left out; when several rules of a zone are in force, the one that started last wins.
///              Dates are compared as ISO 8601 text against the current instant in UTC, as the first version of the app did.
/// </summary>
internal static class ParkingRuleParser
{
	#region Constants
	/// <summary>
	/// The name of this data in failure messages.
	/// </summary>
	internal const string What = "parking rules";
	#endregion

	#region Static Instances
	/// <summary>
	/// The property of a rule for each day of the week.
	/// </summary>
	private static readonly (string Key, DayOfWeek Day)[] DayKeys =
	[
		("mo", DayOfWeek.Monday),
		("tu", DayOfWeek.Tuesday),
		("we", DayOfWeek.Wednesday),
		("th", DayOfWeek.Thursday),
		("fr", DayOfWeek.Friday),
		("sa", DayOfWeek.Saturday),
		("so", DayOfWeek.Sunday),
	];

	/// <summary>
	/// The zone type for each type text of the source.
	/// </summary>
	private static readonly Dictionary<string, ZoneType> ZoneTypes = new(StringComparer.Ordinal)
	{
		["FREE"] = ZoneType.Free,
		["PAYABLE"] = ZoneType.Payable,
		["DEROGATION"] = ZoneType.Derogation,
		["DISK"] = ZoneType.Disk,
		["RESIDENTS"] = ZoneType.Residents,
		["RESERVED"] = ZoneType.Reserved,
	};
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the answer of the rules request.
	/// </summary>
	/// <param name="json">Response body of the parking rules request.</param>
	/// <param name="now">The current moment.</param>
	/// <returns>The rules in force by zone code, or a <c>ParkingRules.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyDictionary<string, ParkingRule>> Parse(string json, DateTimeOffset now)
	{
		var instant = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
		return GeoJson.ReadFeatures<IReadOnlyDictionary<string, ParkingRule>>(json, SourceKind.ParkingRules, What, FeatureFilter.All, features => InForce(features, instant));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Keeps the rules in force at the instant, by zone code.
	/// </summary>
	/// <param name="features">Rule features returned by the service.</param>
	/// <param name="instant">Current instant as ISO text, compared with the rule validity.</param>
	/// <returns>The rules in force by zone code.</returns>
	private static Dictionary<string, ParkingRule> InForce(IReadOnlyList<JsonElement> features, string instant)
	{
		var rules = new Dictionary<string, (ParkingRule Rule, string ActiveFrom)>(StringComparer.Ordinal);
		foreach (var properties in features.Select(GeoJson.Properties).Where(p => IsInForce(p, instant)))
		{
			var code = JsonValues.Text(properties, "code")!;
			var activeFrom = JsonValues.Text(properties, "active_from") ?? string.Empty;
			if (!rules.TryGetValue(code, out var previous) || string.CompareOrdinal(activeFrom, previous.ActiveFrom) > 0)
			{
				rules[code] = (ToRule(properties), activeFrom);
			}
		}

		return rules.ToDictionary(entry => entry.Key, entry => entry.Value.Rule, StringComparer.Ordinal);
	}

	/// <summary>
	/// Determines whether the rule is in force at the instant.
	/// </summary>
	/// <param name="properties">The properties of the feature.</param>
	/// <param name="instant">Current instant as ISO text, compared with the rule validity.</param>
	/// <returns><see langword="true"/> when the rule is in force at the instant; otherwise <see langword="false"/>.</returns>
	private static bool IsInForce(JsonElement properties, string instant)
	{
		var disabledOn = JsonValues.Text(properties, "disabled_on");
		var activeFrom = JsonValues.Text(properties, "active_from");
		return !string.IsNullOrEmpty(JsonValues.Text(properties, "code"))
			&& !JsonValues.IsNumber(properties, "active", 0)
			&& !(!string.IsNullOrEmpty(disabledOn) && string.CompareOrdinal(disabledOn, instant) <= 0)
			&& !(!string.IsNullOrEmpty(activeFrom) && string.CompareOrdinal(activeFrom, instant) > 0);
	}

	/// <summary>
	/// Converts the properties of a feature into a rule.
	/// </summary>
	/// <param name="properties">The properties of the feature.</param>
	/// <returns>Parking rule read from the feature properties.</returns>
	private static ParkingRule ToRule(JsonElement properties)
	{
		var type = JsonValues.Text(properties, "type");
		return new ParkingRule(
			type is not null && ZoneTypes.TryGetValue(type, out var zoneType) ? zoneType : null,
			JsonValues.Text(properties, "name"),
			DayKeys.Where(day => JsonValues.IsNumber(properties, day.Key, 1)).Select(day => day.Day).ToHashSet(),
			JsonValues.Text(properties, "start_hour"),
			JsonValues.Text(properties, "end_hour"),
			JsonValues.IsNumber(properties, "special_day", 1),
			JsonValues.Decimal(properties, "charge60"),
			(int?)JsonValues.Number(properties, "free_time"),
			(int?)JsonValues.Number(properties, "max_time"),
			JsonValues.Decimal(properties, "fee"));
	}
	#endregion
}
