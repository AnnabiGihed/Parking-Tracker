using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Shared.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Zones;

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

	/// <summary>
	/// The length of an ISO date.
	/// </summary>
	private const int IsoDateLength = 10;
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the roadworks answer.
	/// </summary>
	/// <param name="json">Response body of the roadworks request.</param>
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
	/// <param name="json">Response body of the traffic events request.</param>
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
	[GeneratedRegex("^(?<day>[0-9]{2})/(?<month>[0-9]{2})/(?<year>[0-9]{4})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex DayMonthYear();

	/// <summary>
	/// Converts a feature of the sites layer into a work item.
	/// </summary>
	/// <param name="feature">The GeoJSON feature object.</param>
	/// <param name="today">Today's date as ISO text, compared with the work period.</param>
	/// <returns>The work item, or <see langword="null"/> when it is not in force.</returns>
	private static WorkItem? ToSite(JsonElement feature, string today)
	{
		var properties = GeoJson.Properties(feature);
		var start = ParseDayMonthYear(JsonValues.Text(properties, "str_start_date"));
		var end = ParseDayMonthYear(JsonValues.Text(properties, "str_end_date"));
		if ((end is not null && string.CompareOrdinal(end, today) < 0) || (start is not null && string.CompareOrdinal(start, today) > 0))
		{
			return null;
		}

		var title = FirstNonEmpty(JsonValues.Text(properties, "nature_fr")) ?? "Roadworks";
		var where = FirstNonEmpty(JsonValues.Text(properties, "road_impl_fr"), JsonValues.Text(properties, "nom_fr")) ?? string.Empty;
		return Create(feature, WorkKind.Works, new WorkText(title, where, JsonValues.Text(properties, "pilote_fr") ?? string.Empty, JsonValues.Text(properties, "link_fr") ?? string.Empty), new WorkPeriod(start, end));
	}

	/// <summary>
	/// Converts a feature of the events layer into a work item.
	/// </summary>
	/// <param name="feature">The GeoJSON feature object.</param>
	/// <param name="instant">Current instant as ISO text, compared with the event period.</param>
	/// <returns>The work item, or <see langword="null"/> when it is not in force.</returns>
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
		var from = string.IsNullOrEmpty(startTime) ? null : startTime[..Math.Min(IsoDateLength, startTime.Length)];
		var text = new WorkText(title, FirstNonEmpty(JsonValues.Text(properties, "location_fr")) ?? string.Empty, JsonValues.Text(properties, "consequences_fr") ?? string.Empty, string.Empty);
		return Create(feature, WorkKind.Event, text, new WorkPeriod(from, until));
	}

	/// <summary>
	/// Creates a work item from the parts the callers read.
	/// </summary>
	/// <param name="feature">The GeoJSON feature object.</param>
	/// <param name="kind">Kind of work item.</param>
	/// <param name="text">What the item says about itself.</param>
	/// <param name="period">The first and last day of the item.</param>
	/// <returns>The work item.</returns>
	private static WorkItem Create(JsonElement feature, WorkKind kind, WorkText text, WorkPeriod period)
	{
		if (GeoJson.IsPointShaped(feature))
		{
			var points = GeoJson.ReadPoints(feature).Select(WebMercator.ToGeo).ToList();
			return new WorkItem(kind, points, null, text.Title, text.Where, period.To, period.From, text.By, text.Link);
		}

		return new WorkItem(kind, null, GeoJson.ReadShape(feature), text.Title, text.Where, period.To, period.From, text.By, text.Link);
	}

	/// <summary>
	/// Converts a date written as day, month and year into the ISO form.
	/// </summary>
	/// <param name="text">The text to read.</param>
	/// <returns>The ISO date, or <see langword="null"/> when the text is not such a date.</returns>
	private static string? ParseDayMonthYear(string? text)
	{
		var match = DayMonthYear().Match(text ?? string.Empty);
		return match.Success ? $"{match.Groups["year"].Value}-{match.Groups["month"].Value}-{match.Groups["day"].Value}" : null;
	}

	/// <summary>
	/// Picks the first value that is not empty.
	/// </summary>
	/// <param name="values">Candidate texts, in order of preference.</param>
	/// <returns>The first non-empty value, or <see langword="null"/> when there is none.</returns>
	private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrEmpty(value));
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The words of a work item, read from whichever fields the roadworks or event layer fills.
	/// </summary>
	/// <param name="Title">What it is.</param>
	/// <param name="Where">Where it is, in words.</param>
	/// <param name="By">Who runs it, or what it changes.</param>
	/// <param name="Link">Address of a page with details, or empty.</param>
	private sealed record WorkText(string Title, string Where, string By, string Link);

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The days a work item runs, kept as ISO text because the parser compares dates as text.
	/// </summary>
	/// <param name="From">The first day as ISO text, or <see langword="null"/>.</param>
	/// <param name="To">The last day as ISO text, or <see langword="null"/>.</param>
	private sealed record WorkPeriod(string? From, string? To);
	#endregion
}
