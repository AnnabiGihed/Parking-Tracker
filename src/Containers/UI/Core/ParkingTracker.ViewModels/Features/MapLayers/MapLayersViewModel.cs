using System.Globalization;
using ParkingTracker.Application.Features.MapLayers.Queries.GetMapLayers;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.GetAreaStatuses;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The map layers: keeps the switches of the layers popover, reloads the layers 700 ms after the map stopped moving
///              (and every two minutes), asks the Application for the street colours and the other layers, and hands every layer
///              to the screen as a <see cref="MapLayerUpdateEventArgs"/>. A layer that fails does not stop the others; the
///              status line names it. Only the newest load is shown.
/// </summary>
public sealed class MapLayersViewModel : ViewModelBase, IDisposable
{
	#region Constants
	/// <summary>
	/// The name of the layer of street colours.
	/// </summary>
	public const string StreetsLayer = "streets";

	/// <summary>
	/// The name of the layer of the outlines of the areas on the card.
	/// </summary>
	public const string AreasLayer = GetMapLayersResponse.AreasLayer;

	/// <summary>
	/// The share of the screen loaded beyond each edge.
	/// </summary>
	private const double LoadPadding = 0.1;
	#endregion

	#region Fields
	/// <summary>
	/// Sends queries to the Application.
	/// </summary>
	private readonly ISender sender;

	/// <summary>
	/// Port to the saved switches and the card.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// Port to the connection status.
	/// </summary>
	private readonly INetworkStatus network;

	/// <summary>
	/// Opens the links of the popups.
	/// </summary>
	private readonly ILinkLauncher launcher;

	/// <summary>
	/// Clock for the debounce and the time on the status line.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// Cancels the loads when the screen goes away.
	/// </summary>
	private readonly CancellationTokenSource lifetime = new();

	/// <summary>
	/// The timer that waits for the map to stand still.
	/// </summary>
	private ITimer? debounce;

	/// <summary>
	/// The timer that keeps the layers fresh.
	/// </summary>
	private ITimer? refresh;

	/// <summary>
	/// The area and zoom level the map last reported.
	/// </summary>
	private (MapBounds Bounds, double Zoom)? view;

	/// <summary>
	/// Counts the loads, so that an older answer is dropped.
	/// </summary>
	private int sequence;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MapLayersViewModel"/> class; the saved switches are read by <see cref="LoadSwitchesAsync"/>.
	/// </summary>
	/// <param name="sender">Sends queries to the Application.</param>
	/// <param name="settings">Keeps the switches.</param>
	/// <param name="network">Tells whether the device is online.</param>
	/// <param name="launcher">Opens the links of the popups.</param>
	/// <param name="clock">Clock for the debounce and the status line.</param>
	public MapLayersViewModel(ISender sender, ISettingsStore settings, INetworkStatus network, ILinkLauncher launcher, TimeProvider clock)
	{
		this.sender = sender;
		this.settings = settings;
		this.network = network;
		this.launcher = launcher;
		this.clock = clock;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the switches of the layers popover.
	/// </summary>
	public LayerSettings Switches { get; private set; } = new();

	/// <summary>
	/// Gets a value indicating whether the official zone colours are shown as one image under the other layers.
	/// </summary>
	public bool ZoneImageVisible { get; private set; }

	/// <summary>
	/// Gets the line under the layers popover: what is shown, or what went wrong.
	/// </summary>
	public string StatusText { get; private set; } = string.Empty;
	#endregion

	#region Events
	/// <summary>
	/// Occurs when a layer has new content, or none to clear it.
	/// </summary>
	public event EventHandler<MapLayerUpdateEventArgs>? LayerUpdated;
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads the saved switches. Call it once when the screen starts.
	/// </summary>
	/// <returns>A task that completes when the switches were read.</returns>
	public async Task LoadSwitchesAsync()
	{
		Switches = await settings.GetLayersAsync().ConfigureAwait(false);
		NotifyChanged();
	}

	/// <summary>
	/// Starts the timer that keeps the layers fresh. Call it once when the screen is ready.
	/// </summary>
	public void Start()
	{
		refresh ??= clock.CreateTimer(OnRefreshTimer, null, LayerRules.RefreshInterval, LayerRules.RefreshInterval);
	}

	/// <summary>
	/// Handles a move of the map: remembers the area and loads the layers when the map has stood still for 700 ms.
	/// </summary>
	/// <param name="bounds">The area the map shows.</param>
	/// <param name="zoom">The zoom level.</param>
	public void HandleViewChanged(MapBounds bounds, double zoom)
	{
		view = (bounds, zoom);
		debounce?.Dispose();
		debounce = clock.CreateTimer(OnRefreshTimer, null, LayerRules.Debounce, Timeout.InfiniteTimeSpan);
	}

	/// <summary>
	/// Handles a switch of the layers popover: saves it and loads the layers at once.
	/// </summary>
	/// <param name="layer">The name of the layer: <c>streets</c>, <c>spaces</c>, <c>works</c>, <c>carparks</c> or <c>chargers</c>.</param>
	/// <param name="isOn">Whether the switch is on.</param>
	/// <returns>A task that completes when the layers were loaded.</returns>
	public async Task SetLayerAsync(string layer, bool isOn)
	{
		Switches = layer switch
		{
			StreetsLayer => Switches with { Streets = isOn },
			"spaces" => Switches with { Spaces = isOn },
			"works" => Switches with { Works = isOn },
			"carparks" => Switches with { CarParks = isOn },
			"chargers" => Switches with { Chargers = isOn },
			_ => Switches,
		};
		await settings.SaveLayersAsync(Switches).ConfigureAwait(false);
		NotifyChanged();
		await RefreshAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Loads the layers for the area the map shows. Without a card, a reported area or a connection, nothing is loaded.
	/// </summary>
	/// <returns>A task that completes when the layers were handed to the screen.</returns>
	public async Task RefreshAsync()
	{
		if (await settings.GetCardAsync().ConfigureAwait(false) is null || view is not var (bounds, zoom))
		{
			return;
		}

		var mine = ++sequence;
		var active = LayerRules.ActiveAt(Switches, zoom);
		ZoneImageVisible = network.IsOnline && LayerRules.ShowsZoneImage(Switches, zoom);
		if (!network.IsOnline)
		{
			ClearAll();
			StatusText = "offline: no live layers";
			NotifyChanged();
			return;
		}

		ClearInactive(active);
		StatusText = "loading live…";
		NotifyChanged();
		var failures = await LoadAsync(bounds.Pad(LoadPadding), zoom, active, mine).ConfigureAwait(false);
		if (mine != sequence)
		{
			return;
		}

		StatusText = failures.Count > 0 ? "some layers failed: " + string.Join("; ", failures) : OkText(zoom);
		NotifyChanged();
	}

	/// <summary>
	/// Handles a button of a map popup: opens Waze, Google Maps or a web or phone link through the link launcher. A value that is
	/// not a position or an <c>http</c>, <c>https</c> or <c>tel</c> address is ignored, because it comes from the page.
	/// </summary>
	/// <param name="kind">The action name, see <see cref="MapLayerDrawings.WazeAction"/>.</param>
	/// <param name="id">The value of the button: a position or an address.</param>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	public Task HandlePopupActionAsync(string kind, string id)
	{
		var link = kind switch
		{
			MapLayerDrawings.WazeAction when TryReadPoint(id, out var point) => NavigationLinks.Waze(point),
			MapLayerDrawings.GoogleMapsAction when TryReadPoint(id, out var point) => NavigationLinks.GoogleMapsDriving(point),
			MapLayerDrawings.LinkAction when Uri.TryCreate(id, UriKind.Absolute, out var address) && (address.Scheme is "http" or "https" or "tel") => address,
			_ => null,
		};
		return link is null ? Task.CompletedTask : launcher.OpenAsync(link, lifetime.Token);
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		debounce?.Dispose();
		refresh?.Dispose();
		lifetime.Cancel();
		lifetime.Dispose();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Starts a load when a timer fires; the load handles its own failures.
	/// </summary>
	/// <param name="state">Not used.</param>
	private void OnRefreshTimer(object? state)
	{
		_ = RefreshAsync();
	}

	/// <summary>
	/// Loads the active layers and hands each to the screen, unless a newer load started.
	/// </summary>
	/// <param name="box">The area to load.</param>
	/// <param name="zoom">The zoom level.</param>
	/// <param name="active">The layers to load.</param>
	/// <param name="mine">The number of this load.</param>
	/// <returns>The reasons of the layers that failed, named by layer.</returns>
	private async Task<List<string>> LoadAsync(MapBounds box, double zoom, LayerSettings active, int mine)
	{
		var failures = new List<string>();
		var now = clock.GetLocalNow();
		var streets = active.Streets ? LoadStreetsAsync(box, zoom) : Task.FromResult<Result<IReadOnlyList<SurfaceStatus>>?>(null);
		var others = sender.Send(new GetMapLayersQuery(box.South, box.West, box.North, box.East, active), lifetime.Token);
		await Task.WhenAll(streets, others).ConfigureAwait(false);
		if (mine != sequence)
		{
			return failures;
		}

		var streetResult = await streets.ConfigureAwait(false);
		if (streetResult is { IsFailure: true } failedStreets)
		{
			failures.Add($"{StreetsLayer}: {failedStreets.Error.Message}");
			Publish(StreetsLayer, [], []);
		}
		else if (streetResult is { } okStreets)
		{
			Publish(StreetsLayer, MapLayerDrawings.Streets(okStreets.Value, now), []);
		}

		var layers = await others.ConfigureAwait(false);
		if (layers.IsFailure)
		{
			failures.Add(layers.Error.Message);
			return failures;
		}

		PublishOthers(layers.Value, failures);
		return failures;
	}

	/// <summary>
	/// Asks for the street colours.
	/// </summary>
	/// <param name="box">The area to load.</param>
	/// <param name="zoom">The zoom level, which decides whether house numbers are fetched.</param>
	/// <returns>The result, as a nullable so that it fits the same variable as the answer for a layer that is not shown.</returns>
	private async Task<Result<IReadOnlyList<SurfaceStatus>>?> LoadStreetsAsync(MapBounds box, double zoom)
	{
		return await sender.Send(new GetAreaStatusesQuery(box.South, box.West, box.North, box.East, LayerRules.NeedsHouseNumbers(zoom)), lifetime.Token).ConfigureAwait(false);
	}

	/// <summary>
	/// Hands the layers of one map-layers answer to the screen and collects the layers that failed.
	/// </summary>
	/// <param name="layers">The answer of the Application.</param>
	/// <param name="failures">Collects the reasons of the layers that failed.</param>
	private void PublishOthers(GetMapLayersResponse layers, List<string> failures)
	{
		Publish(AreasLayer, MapLayerDrawings.Areas(layers.MyAreas), []);
		Publish(GetMapLayersResponse.SpacesLayer, [], MapLayerDrawings.Spaces(layers.Spaces));
		Publish(GetMapLayersResponse.WorksLayer, MapLayerDrawings.WorkAreas(layers.Works), MapLayerDrawings.WorkPoints(layers.Works));
		Publish(GetMapLayersResponse.CarParksLayer, [], MapLayerDrawings.CarParks(layers.CarParks));
		Publish(GetMapLayersResponse.ChargersLayer, [], MapLayerDrawings.Chargers(layers.Chargers));
		failures.AddRange(layers.Failures.Select(failure => $"{failure.Key}: {failure.Value}"));
	}

	/// <summary>
	/// Hands one layer to the screen.
	/// </summary>
	/// <param name="layer">The name of the layer.</param>
	/// <param name="surfaces">The areas of the layer.</param>
	/// <param name="points">The markers of the layer.</param>
	private void Publish(string layer, IReadOnlyList<MapSurface> surfaces, IReadOnlyList<MapPoint> points)
	{
		LayerUpdated?.Invoke(this, new MapLayerUpdateEventArgs(layer, surfaces, points));
	}

	/// <summary>
	/// Clears the layers that are off or zoomed out, so that nothing stale stays on the map.
	/// </summary>
	/// <param name="active">The layers that are shown.</param>
	private void ClearInactive(LayerSettings active)
	{
		if (!active.Streets)
		{
			Publish(StreetsLayer, [], []);
			Publish(AreasLayer, [], []);
		}

		if (!active.Spaces)
		{
			Publish(GetMapLayersResponse.SpacesLayer, [], []);
		}

		if (!active.Works)
		{
			Publish(GetMapLayersResponse.WorksLayer, [], []);
		}

		if (!active.CarParks)
		{
			Publish(GetMapLayersResponse.CarParksLayer, [], []);
		}

		if (!active.Chargers)
		{
			Publish(GetMapLayersResponse.ChargersLayer, [], []);
		}
	}

	/// <summary>
	/// Clears every layer.
	/// </summary>
	private void ClearAll() => ClearInactive(new LayerSettings(false, false, false, false, false));

	/// <summary>
	/// Writes the status line for a load without failures.
	/// </summary>
	/// <param name="zoom">The zoom level.</param>
	/// <returns>The text.</returns>
	private string OkText(double zoom)
	{
		var updated = ClockText.Time(clock.GetLocalNow());
		if (zoom < LayerRules.StreetsMinZoom)
		{
			return "Official zone colours (red, grey, green, blue…). Zoom in to see where your card works.";
		}

		return zoom < LayerRules.SpacesMinZoom
			? $"Where your card works · updated {updated} · zoom in for parking spaces"
			: $"Where your card works + parking spaces · updated {updated}";
	}

	/// <summary>
	/// Reads a position written as <c>latitude,longitude</c>.
	/// </summary>
	/// <param name="text">The text from a popup button.</param>
	/// <param name="point">The position that was read.</param>
	/// <returns><see langword="true"/> when the text is a valid position.</returns>
	private static bool TryReadPoint(string text, out GeoPoint point)
	{
		var parts = text.Split(',');
		point = default;
		if (parts.Length != 2
			|| !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
			|| !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
			|| latitude is < -90 or > 90
			|| longitude is < -180 or > 180)
		{
			return false;
		}

		point = new GeoPoint(latitude, longitude);
		return true;
	}
	#endregion
}
