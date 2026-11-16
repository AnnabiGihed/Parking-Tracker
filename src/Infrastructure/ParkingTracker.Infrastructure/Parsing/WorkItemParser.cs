using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Zones;
using ParkingTracker.Infrastructure.Sources;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Parsing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the Brussels Mobility roadworks and traffic-event answers into the items that are running at a moment.
///              Dates are compared as text, as the first version of the app did: roadworks days as <c>yyyy-MM-dd</c> against
///              today's local date, event times as ISO 8601 text against the current instant in UTC.
/// </summary>
internal static partial class WorkItemParser
{
	#region Constants
	/// <summary>
	/// The name of the roadworks data in failure messages.
	/// </summary>
	internal const string RoadworksWhat = "roadworks";

	/// <summary>
	/// The name of the traffic events data in failure messages.
	/// </summary>
	internal const string EventsWhat = "traffic events";

	private const int IsoDateLength = 10;
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the roadworks answer.
	/// </summary>
	/// <param name="json">The answer.</param>
	/// <param name="now">The current moment on the clock of the device; its local date decides which sites are running.</param>
	/// <returns>The sites running today, or a <c>Mobility.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyList<WorkItem>> ParseRoadworks(string json, DateTimeOffset now)
	{
		var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		return GeoJson.ReadFeatures<IReadOnlyList<WorkItem>>(json, SourceKind.Mobility, RoadworksWhat, FeatureFilter.WithGeometry, features => features
			.Select(feature => ToSite(feature, today))
			.OfType<WorkItem>()
			.ToList());
	}

	/// <summary>
	/// Reads the traffic events answer.
	/// </summary>
	/// <param name="json">The answer.</param>
	/// <param name="now">The current moment; events that ended or have not started at it are left out.</param>
	/// <returns>The events running now, or a <c>Mobility.BadResponse</c> failure.</returns>
	internal static Result<IReadOnlyList<WorkItem>> ParseEvents(string json, DateTimeOffset now)
	{
		var instant = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
		return GeoJson.ReadFeatures<IReadOnlyList<WorkItem>>(json, SourceKind.Mobility, EventsWhat, FeatureFilter.WithGeometry, features => features
			.Select(feature => ToEvent(feature, instant))
			.OfType<WorkItem>()
			.ToList());
	}
	#endregion

	#region Private Helpers
	[GeneratedRegex("^([0-9]{2})/([0-9]{2})/([0-9]{4})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex DayMonthYear();

	private static WorkItem? ToSite(JsonElement feature, string today)
	{
		var properties = GeoJson.Properties(feature);
		var start = ParseDayMonthYear(JsonValues.Text(properties, "str_start_date"));
		var end = ParseDayMonthYear(JsonValues.Text(properties, "str_end_date"));
		if ((end is not null && string.CompareOrdinal(end, today) < 0) || (start is not null && string.CompareOrdinal(start, today) > 0))
		{
			return null;
		}

		return Create(feature, WorkKind.Works, FirstNonEmpty(JsonValues.Text(properties, "nature_fr")) ?? "Roadworks", FirstNonEmpty(JsonValues.Text(properties, "road_impl_fr"), JsonValues.Text(properties, "nom_fr")) ?? string.Empty, end);
	}

	private static WorkItem? ToEvent(JsonElement feature, string instant)
	{
		var properties = GeoJson.Properties(feature);
		var startTime = JsonValues.Text(properties, "start_time");
		var endTime = JsonValues.Text(properties, "end_time");
		var inactive = JsonValues.Property(properties, "is_active").ValueKind == JsonValueKind.False;
		if (inactive || (!string.IsNullOrEmpty(endTime) && string.CompareOrdinal(endTime, instant) < 0) || (!string.IsNullOrEmpty(startTime) && string.CompareOrdinal(startTime, instant) > 0))
		{
			return null;
		}

		var title = (FirstNonEmpty(JsonValues.Text(properties, "type_fr")) ?? "Event").Trim();
		var until = string.IsNullOrEmpty(endTime) ? null : endTime[..Math.Min(IsoDateLength, endTime.Length)];
		return Create(feature, WorkKind.Event, title, FirstNonEmpty(JsonValues.Text(properties, "location_fr")) ?? string.Empty, until);
	}

	private static WorkItem Create(JsonElement feature, WorkKind kind, string title, string where, string? until)
	{
		if (GeoJson.IsPointShaped(feature))
		{
			var points = GeoJson.ReadPoints(feature).Select(WebMercator.ToGeo).ToList();
			return new WorkItem(kind, points, null, title, where, until);
		}

		return new WorkItem(kind, null, GeoJson.ReadShape(feature), title, where, until);
	}

	private static string? ParseDayMonthYear(string? text)
	{
		var match = DayMonthYear().Match(text ?? string.Empty);
		return match.Success ? $"{match.Groups[3].Value}-{match.Groups[2].Value}-{match.Groups[1].Value}" : null;
	}

	private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrEmpty(value));
	#endregion
}
