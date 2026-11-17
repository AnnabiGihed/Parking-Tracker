using ParkingTracker.Domain.Features.MapLayers.ValueObjects;

namespace ParkingTracker.ViewModels.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The zoom levels and timings of the map layers, with the values of the first version of the map: zoomed out the
///              official zone colours are shown as one image; from zoom 15 the colours per street surface, the works and the
///              charging points; from 16 the house-number detail of City cards; from 17 the parking spaces; car parks from 14.
/// </summary>
public static class LayerRules
{
	#region Constants
	/// <summary>
	/// From this zoom the colour of each street surface for the user's card is shown; below it the zone image is shown.
	/// </summary>
	public const int StreetsMinZoom = 15;

	/// <summary>
	/// From this zoom the house numbers are fetched, which colour streets a City card covers in part.
	/// </summary>
	public const int HouseNumberMinZoom = 16;

	/// <summary>
	/// From this zoom the individual parking spaces are shown.
	/// </summary>
	public const int SpacesMinZoom = 17;

	/// <summary>
	/// From this zoom the roadworks and events are shown.
	/// </summary>
	public const int WorksMinZoom = 15;

	/// <summary>
	/// From this zoom the car parks are shown.
	/// </summary>
	public const int CarParksMinZoom = 14;

	/// <summary>
	/// From this zoom the charging points are shown.
	/// </summary>
	public const int ChargersMinZoom = 15;
	#endregion

	#region Static Instances
	/// <summary>
	/// How long the map must stand still before the layers are loaded again.
	/// </summary>
	public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(700);

	/// <summary>
	/// How often the layers are loaded again while the map stands still, to keep them fresh.
	/// </summary>
	public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(120);
	#endregion

	#region Public Methods
	/// <summary>
	/// Gives the layers that are switched on and visible at a zoom level.
	/// </summary>
	/// <param name="settings">The switches of the user.</param>
	/// <param name="zoom">The zoom level of the map.</param>
	/// <returns>The layers to load; a layer below its zoom level is off.</returns>
	public static LayerSettings ActiveAt(LayerSettings settings, double zoom)
	{
		ArgumentNullException.ThrowIfNull(settings);
		return new LayerSettings(
			settings.Streets && zoom >= StreetsMinZoom,
			settings.Spaces && zoom >= SpacesMinZoom,
			settings.Works && zoom >= WorksMinZoom,
			settings.CarParks && zoom >= CarParksMinZoom,
			settings.Chargers && zoom >= ChargersMinZoom);
	}

	/// <summary>
	/// Tells whether the official zone colours are shown as one image, which is cheap at any zoom.
	/// </summary>
	/// <param name="settings">The switches of the user.</param>
	/// <param name="zoom">The zoom level of the map.</param>
	/// <returns><see langword="true"/> when the street layer is on and the map is zoomed out.</returns>
	public static bool ShowsZoneImage(LayerSettings settings, double zoom)
	{
		ArgumentNullException.ThrowIfNull(settings);
		return settings.Streets && zoom < StreetsMinZoom;
	}

	/// <summary>
	/// Tells whether house numbers are fetched, to colour the streets a City card covers in part.
	/// </summary>
	/// <param name="zoom">The zoom level of the map.</param>
	/// <returns><see langword="true"/> from zoom 16.</returns>
	public static bool NeedsHouseNumbers(double zoom) => zoom >= HouseNumberMinZoom;
	#endregion
}
