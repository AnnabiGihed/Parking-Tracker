using System.Globalization;
using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Infrastructure.Features.Shared.Http;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds the request addresses of the live sources, with the parameters the first version of the app sent: WFS 2.0.0
///              GeoJSON in EPSG:3857 with an EPSG:4326 bounding box, no cache.
/// </summary>
internal static class SourceRequests
{
	#region Constants
	/// <summary>
	/// The parking.brussels WFS service.
	/// </summary>
	internal const string ParkingBrusselsWfs = "https://ows.parking.brussels/ws/ParkingBrussels/ows";

	/// <summary>
	/// The UrbIS WFS service of Brussels Mobility.
	/// </summary>
	internal const string UrbisWfs = "https://data.mobility.brussels/geoserver/bm_urbis/wfs";

	/// <summary>
	/// The Brussels Mobility WFS service (roadworks, events, car parks, charging points).
	/// </summary>
	internal const string MobilityWfs = "https://data.mobility.brussels/geoserver/ows";

	/// <summary>
	/// The Brussels Mobility parking rules API.
	/// </summary>
	internal const string RulesApi = "https://api.mobility.brussels/datasets/v1/parking/collections/regulations_rules/items/";

	/// <summary>
	/// The OpenStreetMap Nominatim search service.
	/// </summary>
	internal const string NominatimSearch = "https://nominatim.openstreetmap.org/search";

	/// <summary>
	/// The query every WFS request starts with.
	/// </summary>
	private const string WfsPrefix = "service=WFS&version=2.0.0&request=GetFeature&outputFormat=application/json";
	#endregion

	#region Internal Methods
	/// <summary>
	/// Builds the request for the street surfaces of the regulation zones in a box.
	/// </summary>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>Request address of the zones layer for the box.</returns>
	internal static string Zones(GeoBox box)
	{
		return $"{ParkingBrusselsWfs}?{WfsPrefix}&typeNames=ParkingBrussels:regulation_zones&srsName=EPSG:3857&count=3000&bbox={BoundingBox(box)}";
	}

	/// <summary>
	/// Builds the request for the individual on-street parking spaces in a box.
	/// </summary>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>Request address of the reserved spaces layer for the box.</returns>
	internal static string Spaces(GeoBox box) => Layer(ParkingBrusselsWfs, "ParkingBrussels:on_street", box);

	/// <summary>
	/// Builds the request for the sector areas (mailles) in a box.
	/// </summary>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>Request address of the sector mailles layer for the box.</returns>
	internal static string Mailles(GeoBox box) => Layer(ParkingBrusselsWfs, "ParkingBrussels:permit_secteurs_mailles", box);

	/// <summary>
	/// Builds the request for the official address points in a box.
	/// </summary>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>Request address of the address points for the box.</returns>
	internal static string AddressPoints(GeoBox box)
	{
		return $"{UrbisWfs}?{WfsPrefix}&typeNames=bm_urbis:urbadm_adpt&srsName=EPSG:3857&propertyName=adrn,geom&count=5000&bbox={BoundingBox(box)}";
	}

	/// <summary>
	/// Builds the request for the roadworks sites in a box.
	/// </summary>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>Request address of the roadworks layer for the box.</returns>
	internal static string Roadworks(GeoBox box) => Layer(MobilityWfs, "bm_osiris:chantiers_actu", box);

	/// <summary>
	/// Builds the request for the traffic events in a box.
	/// </summary>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>Request address of the traffic events layer for the box.</returns>
	internal static string TrafficEvents(GeoBox box) => Layer(MobilityWfs, "bm_traffic:events", box);

	/// <summary>
	/// Builds the request for the public car parks in a box.
	/// </summary>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>Request address of the car parks layer for the box.</returns>
	internal static string CarParks(GeoBox box) => Layer(MobilityWfs, "bm_parking:parkingpublic", box);

	/// <summary>
	/// Builds the request for the charging points in a box.
	/// </summary>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>Request address of the charging points layer for the box.</returns>
	internal static string Chargers(GeoBox box) => Layer(MobilityWfs, "bm_equipment:evse", box);

	/// <summary>
	/// Builds the request for the list of every sector area (code and names, no shapes), for the card form.
	/// </summary>
	/// <returns>Request address of the sector list.</returns>
	internal static string MailleList()
	{
		return $"{ParkingBrusselsWfs}?{WfsPrefix}&typeNames=ParkingBrussels:permit_secteurs_mailles&propertyName=zonecode,namefre,namedut";
	}

	/// <summary>
	/// Builds the request for an address search limited to Belgium and the box of the Brussels-Capital Region, with the details
	/// of each address (road, house number, postcode, municipality) that the list of matches shows.
	/// </summary>
	/// <param name="text">What the user typed.</param>
	/// <param name="limit">The most matches to return.</param>
	/// <returns>Request address of the search.</returns>
	internal static string AddressSearch(string text, int limit = SearchAddressQuery.DefaultLimit)
	{
		return $"{NominatimSearch}?format=jsonv2&addressdetails=1&limit={limit.ToString(CultureInfo.InvariantCulture)}&countrycodes=be&viewbox=4.24,50.92,4.48,50.76&bounded=1&q={Uri.EscapeDataString(text)}";
	}

	/// <summary>
	/// Builds the request for the parking rules.
	/// </summary>
	/// <param name="code">The zone code, or <see langword="null"/> for the rules of every zone.</param>
	/// <returns>Request address of the parking rules, optionally for one zone code.</returns>
	internal static string Rules(string? code)
	{
		return $"{RulesApi}?f=json&limit=500{(string.IsNullOrEmpty(code) ? string.Empty : "&code=" + Uri.EscapeDataString(code))}";
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the address of one layer of a WFS service for a box.
	/// </summary>
	/// <param name="baseAddress">Base address of the WFS service.</param>
	/// <param name="layer">Name of the WFS layer.</param>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>Request address of the layer for the box.</returns>
	private static string Layer(string baseAddress, string layer, GeoBox box)
	{
		return $"{baseAddress}?{WfsPrefix}&typeNames={layer}&srsName=EPSG:3857&count=5000&bbox={BoundingBox(box)}";
	}

	/// <summary>
	/// Formats a bounding box as the WFS service expects it.
	/// </summary>
	/// <param name="box">Bounding box of the area to request.</param>
	/// <returns>The bounding box text.</returns>
	private static string BoundingBox(GeoBox box)
	{
		var culture = CultureInfo.InvariantCulture;
		return $"{box.South.ToString("F6", culture)},{box.West.ToString("F6", culture)},{box.North.ToString("F6", culture)},{box.East.ToString("F6", culture)},urn:ogc:def:crs:EPSG::4326";
	}
	#endregion
}
