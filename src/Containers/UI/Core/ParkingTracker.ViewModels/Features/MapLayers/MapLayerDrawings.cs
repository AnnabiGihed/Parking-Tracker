using System.Net;
using ParkingTracker.Domain.Features.PaidHours.Services;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the data of the map layers into what the map script draws: style keys, rounded coordinates and popup text.
///              Every text that comes from a service is HTML-escaped here, because the script puts the popup into the page as
///              HTML. The popup buttons carry an action name and a value that the view model reads back
///              (<see cref="MapLayersViewModel.HandlePopupActionAsync"/>).
/// </summary>
public static class MapLayerDrawings
{
	#region Constants
	/// <summary>
	/// The action name of a navigation button that opens Waze.
	/// </summary>
	public const string WazeAction = "waze";

	/// <summary>
	/// The action name of a navigation button that opens Google Maps.
	/// </summary>
	public const string GoogleMapsAction = "gmaps";

	/// <summary>
	/// The action name of a link that opens a web page or a phone number.
	/// </summary>
	public const string LinkAction = "link";

	/// <summary>
	/// The style key of the outline of the areas on the user's card.
	/// </summary>
	public const string AreaStyle = "area";

	/// <summary>
	/// The style key of a roadworks or event area and marker.
	/// </summary>
	public const string WorkStyle = "work";

	/// <summary>
	/// The style key of an ordinary parking space.
	/// </summary>
	public const string SpaceStyle = "space";

	/// <summary>
	/// The style key of a marker that shows its emoji.
	/// </summary>
	public const string MarkerStyle = "marker";

	/// <summary>
	/// The number of decimals kept for coordinates.
	/// </summary>
	private const int CoordinateDecimals = 6;
	#endregion

	#region Public Methods
	/// <summary>
	/// Draws the colour of every street surface. Outside the paid hours of its rule, a paid surface is free for everyone right now
	/// and a part-covered one is covered.
	/// </summary>
	/// <param name="statuses">The status of each street surface.</param>
	/// <param name="now">The moment on the clock of the device.</param>
	/// <returns>The areas to draw, without popup.</returns>
	public static IReadOnlyList<MapSurface> Streets(IReadOnlyList<SurfaceStatus> statuses, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(statuses);
		return [.. statuses.Select(status => new MapSurface(StyleOf(status, now), Shape(status.Surface.Shape), null))];
	}

	/// <summary>
	/// Draws the outline of the areas on the user's card.
	/// </summary>
	/// <param name="areas">The areas.</param>
	/// <returns>The outlines to draw.</returns>
	public static IReadOnlyList<MapSurface> Areas(IReadOnlyList<SectorMaille> areas)
	{
		ArgumentNullException.ThrowIfNull(areas);
		return [.. areas.Select(area => new MapSurface(AreaStyle, Shape(area.Shape), null))];
	}

	/// <summary>
	/// Draws the parking spaces: a marker with a popup for a reserved space, a small dot for an ordinary one.
	/// </summary>
	/// <param name="spaces">The spaces.</param>
	/// <returns>The markers to draw.</returns>
	public static IReadOnlyList<MapPoint> Spaces(IReadOnlyList<ReservedSpace> spaces)
	{
		ArgumentNullException.ThrowIfNull(spaces);
		return [.. spaces.Select(SpaceMarker)];
	}

	/// <summary>
	/// Draws the roadworks and events that are shaped as areas.
	/// </summary>
	/// <param name="works">The roadworks and events.</param>
	/// <returns>The areas to draw, with popup.</returns>
	public static IReadOnlyList<MapSurface> WorkAreas(IReadOnlyList<WorkItem> works)
	{
		ArgumentNullException.ThrowIfNull(works);
		return [.. works.Where(work => work.Surface is not null).Select(work => new MapSurface(WorkStyle, Shape(work.Surface!), WorkPopup(work)))];
	}

	/// <summary>
	/// Draws the roadworks and events that are shaped as points.
	/// </summary>
	/// <param name="works">The roadworks and events.</param>
	/// <returns>The markers to draw, with popup.</returns>
	public static IReadOnlyList<MapPoint> WorkPoints(IReadOnlyList<WorkItem> works)
	{
		ArgumentNullException.ThrowIfNull(works);
		var markers = works.Where(work => work.Points is not null)
			.SelectMany(work => work.Points!.Select(point => Marker(point, "🚧", WorkPopup(work))));
		return [.. markers];
	}

	/// <summary>
	/// Draws the car parks, with a popup that offers navigation.
	/// </summary>
	/// <param name="carParks">The car parks.</param>
	/// <returns>The markers to draw.</returns>
	public static IReadOnlyList<MapPoint> CarParks(IReadOnlyList<CarPark> carParks)
	{
		ArgumentNullException.ThrowIfNull(carParks);
		return [.. carParks.Select(park => Marker(park.Point, "🅿️", CarParkPopup(park)))];
	}

	/// <summary>
	/// Draws the charging points, with a popup that offers navigation.
	/// </summary>
	/// <param name="chargers">The charging points.</param>
	/// <returns>The markers to draw.</returns>
	public static IReadOnlyList<MapPoint> Chargers(IReadOnlyList<ChargingPoint> chargers)
	{
		ArgumentNullException.ThrowIfNull(chargers);
		return [.. chargers.Select(charger => Marker(charger.Point, "⚡", ChargerPopup(charger)))];
	}

	/// <summary>
	/// Converts a shape from Web Mercator to latitude and longitude pairs rounded to six decimals.
	/// </summary>
	/// <param name="shape">The shape of a surface.</param>
	/// <returns>The polygons, each a list of rings of <c>[latitude, longitude]</c> pairs.</returns>
	public static IReadOnlyList<IReadOnlyList<IReadOnlyList<double[]>>> Shape(SurfaceShape shape)
	{
		ArgumentNullException.ThrowIfNull(shape);
		var polygons = shape.Polygons
			.Where(polygon => polygon.Count > 0)
			.Select(polygon => (IReadOnlyList<IReadOnlyList<double[]>>)[.. polygon.Select(ring => (IReadOnlyList<double[]>)[.. ring.Select(vertex => Pair(WebMercator.ToGeo(vertex)))])]);
		return [.. polygons];
	}

	/// <summary>
	/// Draws a marker that shows a label. The label is escaped here as a second line of defence, because it can come from a
	/// service; the map script also shows it as text and never parses it as HTML. An emoji is not changed.
	/// </summary>
	/// <param name="point">Where the marker is.</param>
	/// <param name="label">The emoji shown in the marker.</param>
	/// <param name="popupHtml">The popup, whose text the caller already escaped, or <see langword="null"/> for none.</param>
	/// <returns>The marker, with rounded coordinates.</returns>
	public static MapPoint Marker(GeoPoint point, string label, string? popupHtml)
	{
		return new MapPoint(Round(point.Latitude), Round(point.Longitude), MarkerStyle, EscapeMarkup(label), popupHtml);
	}

	/// <summary>
	/// Escapes a text for the HTML of a popup.
	/// </summary>
	/// <param name="text">The text from a service.</param>
	/// <returns>The escaped text.</returns>
	public static string Escape(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
	#endregion

	#region Private Helpers
	/// <summary>
	/// Escapes the characters that start markup or end an attribute, and leaves every other character, such as an emoji, as it is.
	/// </summary>
	/// <param name="text">The text from a service.</param>
	/// <returns>The text without <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and <c>'</c>.</returns>
	private static string EscapeMarkup(string text)
	{
		return text.Replace("&", "&amp;", StringComparison.Ordinal)
			.Replace("<", "&lt;", StringComparison.Ordinal)
			.Replace(">", "&gt;", StringComparison.Ordinal)
			.Replace("\"", "&quot;", StringComparison.Ordinal)
			.Replace("'", "&#39;", StringComparison.Ordinal);
	}

	/// <summary>
	/// Draws one parking space: a marker with a popup for a reserved space, a small dot for an ordinary one. The label of the
	/// marker comes from a service, so it is escaped here as a second line of defence; the script shows it as text.
	/// </summary>
	/// <param name="space">The space.</param>
	/// <returns>The marker.</returns>
	private static MapPoint SpaceMarker(ReservedSpace space)
	{
		var latitude = Round(space.Point.Latitude);
		var longitude = Round(space.Point.Longitude);
		if (!space.IsReserved)
		{
			return new MapPoint(latitude, longitude, SpaceStyle, null, null);
		}

		var emoji = FirstWord(space.Label) ?? "⛔";
		return Marker(space.Point, emoji, $"<b>{Escape(space.Label ?? "Reserved space")}</b><br>You can't park here with your residents' card.");
	}

	/// <summary>
	/// Gets the style key of a street surface.
	/// </summary>
	/// <param name="status">The status of the surface.</param>
	/// <param name="now">The moment on the clock of the device.</param>
	/// <returns>The style key: the kind in lower case, or <c>freenow</c> outside paid hours.</returns>
	private static string StyleOf(SurfaceStatus status, DateTimeOffset now)
	{
		var kind = status.Kind;
		var payable = kind is SurfaceKind.Pay or SurfaceKind.Red or SurfaceKind.Disc or SurfaceKind.Mixed;
		if (payable && status.Rule is not null && !PaidHours.IsPaidAt(status.Rule, now))
		{
			return kind == SurfaceKind.Mixed ? "covered" : "freenow";
		}

		return kind.ToString().ToLowerInvariant();
	}

	/// <summary>
	/// Builds the popup of a roadworks site or event.
	/// </summary>
	/// <param name="work">The item.</param>
	/// <returns>The popup HTML.</returns>
	private static string WorkPopup(WorkItem work)
	{
		var period = (work.From is null ? string.Empty : "From " + Escape(IsoToDisplay(work.From))) + (work.To is null ? string.Empty : " until " + Escape(IsoToDisplay(work.To)));
		var by = string.IsNullOrEmpty(work.By) ? string.Empty : "<br>" + Escape(work.By);
		var link = string.IsNullOrEmpty(work.Link) ? string.Empty : $"<br><a href=\"#\" data-act=\"{LinkAction}\" data-id=\"{Escape(work.Link)}\">Details</a>";
		return $"<b>🚧 {Escape(work.Title)}</b><br>{Escape(work.Where)}<br>{period}{by}{link}";
	}

	/// <summary>
	/// Builds the popup of a car park.
	/// </summary>
	/// <param name="park">The car park.</param>
	/// <returns>The popup HTML.</returns>
	private static string CarParkPopup(CarPark park)
	{
		var capacity = park.Capacity is null ? string.Empty : Escape(park.Capacity) + " places";
		var disabled = park.DisabledCapacity is null ? string.Empty : ", " + Escape(park.DisabledCapacity) + " ♿";
		var height = park.MaxHeight is null ? string.Empty : "<br>Max height " + Escape(park.MaxHeight) + " m";
		var operatorName = string.IsNullOrEmpty(park.Operator) ? string.Empty : "<br>" + Escape(park.Operator);
		var phone = string.IsNullOrEmpty(park.Phone) ? string.Empty : $" · <a href=\"#\" data-act=\"{LinkAction}\" data-id=\"tel:{Escape(park.Phone)}\">{Escape(park.Phone)}</a>";
		return $"<b>{Escape(park.Name)}</b><br>{Escape(park.Address)}<br>{capacity}{disabled}{height}{operatorName}{phone}{NavigationButtons(park.Point)}";
	}

	/// <summary>
	/// Builds the popup of a charging point.
	/// </summary>
	/// <param name="charger">The charging point.</param>
	/// <returns>The popup HTML.</returns>
	private static string ChargerPopup(ChargingPoint charger)
	{
		return $"<b>⚡ {Escape(charger.Operator ?? "Charging point")}</b><br>{Escape(charger.Points ?? "?")} point(s) · {Escape(charger.Power ?? "?")} V"
			+ $"<br>{Escape(charger.Standard)} {Escape(charger.Type)}{NavigationButtons(charger.Point)}";
	}

	/// <summary>
	/// Builds the Waze and Google Maps buttons of a popup.
	/// </summary>
	/// <param name="point">The destination.</param>
	/// <returns>The HTML of the buttons.</returns>
	private static string NavigationButtons(GeoPoint point)
	{
		var id = NavigationLinks.Pair(point);
		return $"<div class=\"nav-row\"><button type=\"button\" class=\"btn btn-waze\" data-act=\"{WazeAction}\" data-id=\"{id}\">Waze</button>"
			+ $"<button type=\"button\" class=\"btn btn-gmaps\" data-act=\"{GoogleMapsAction}\" data-id=\"{id}\">Google Maps</button></div>";
	}

	/// <summary>
	/// Writes a date in ISO form as day, month and year.
	/// </summary>
	/// <param name="iso">The date as <c>yyyy-MM-dd</c>.</param>
	/// <returns>The date as <c>dd/MM/yyyy</c>.</returns>
	private static string IsoToDisplay(string iso) => string.Join('/', iso.Split('-').Reverse());

	/// <summary>
	/// Gets the first word of a label, which is its emoji.
	/// </summary>
	/// <param name="label">The label, or <see langword="null"/>.</param>
	/// <returns>The first word, or <see langword="null"/> when there is no label.</returns>
	private static string? FirstWord(string? label) => label?.Split(' ')[0];

	/// <summary>
	/// Rounds a coordinate to the precision sent to the map.
	/// </summary>
	/// <param name="degrees">The coordinate.</param>
	/// <returns>The rounded coordinate.</returns>
	private static double Round(double degrees) => Math.Round(degrees, CoordinateDecimals);

	/// <summary>
	/// Writes a position as a latitude and longitude pair.
	/// </summary>
	/// <param name="point">The position.</param>
	/// <returns>The rounded pair.</returns>
	private static double[] Pair(GeoPoint point) => [Round(point.Latitude), Round(point.Longitude)];
	#endregion
}
