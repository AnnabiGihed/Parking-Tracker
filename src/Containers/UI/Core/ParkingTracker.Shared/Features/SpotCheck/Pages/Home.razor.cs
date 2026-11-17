using Microsoft.AspNetCore.Components;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Shared.Features.MapLayers.Components;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Features.Welcome;

namespace ParkingTracker.Shared.Features.SpotCheck.Pages;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The only page of the app: the map with its controls, the search bar, the bottom sheet and the dialogs, laid out as
///              <c>index.html</c> did. It joins the view models to the map: it draws the markers and layers the view models
///              publish, carries out their requests to move the map, and passes the map's events back to them. It holds no
///              parking rules.
/// </summary>
public sealed partial class Home : ComponentBase, IDisposable
{
	#region Injected Services
	/// <summary>
	/// Gets or sets the spot check, which follows the position and holds the answer.
	/// </summary>
	[Inject]
	public SpotCheckViewModel Spot { get; set; } = null!;

	/// <summary>
	/// Gets or sets the card.
	/// </summary>
	[Inject]
	public CardViewModel Card { get; set; } = null!;

	/// <summary>
	/// Gets or sets the parked car.
	/// </summary>
	[Inject]
	public CarViewModel Car { get; set; } = null!;

	/// <summary>
	/// Gets or sets the map layers.
	/// </summary>
	[Inject]
	public MapLayersViewModel Layers { get; set; } = null!;

	/// <summary>
	/// Gets or sets the welcome dialog.
	/// </summary>
	[Inject]
	public WelcomeViewModel Welcome { get; set; } = null!;

	/// <summary>
	/// Gets or sets the location permission flow.
	/// </summary>
	[Inject]
	public LocationPermissionViewModel Permission { get; set; } = null!;
	#endregion

	#region Fields
	/// <summary>
	/// The map component, which holds the script.
	/// </summary>
	private MapView? map;

	/// <summary>
	/// The element of the bottom sheet, whose height the map script follows.
	/// </summary>
	private ElementReference sheet;

	/// <summary>
	/// Whether the bottom sheet is collapsed to its headline.
	/// </summary>
	private bool sheetCompact;

	/// <summary>
	/// Whether the layers popover is open.
	/// </summary>
	private bool layersOpen;

	/// <summary>
	/// Whether the map exists, so that the markers can be drawn.
	/// </summary>
	private bool mapReady;

	/// <summary>
	/// The fix of the user as last drawn on the map.
	/// </summary>
	private PositionFix? drawnMe;

	/// <summary>
	/// The pin as last drawn on the map.
	/// </summary>
	private GeoPoint? drawnPin;

	/// <summary>
	/// The nearest free spot as last drawn on the map.
	/// </summary>
	private GeoPoint? drawnTarget;

	/// <summary>
	/// The parked car as last drawn on the map.
	/// </summary>
	private GeoPoint? drawnCar;

	/// <summary>
	/// Whether the zone image was last shown.
	/// </summary>
	private bool drawnZoneImage;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override async Task OnInitializedAsync()
	{
		await Card.RefreshLineAsync();
		await Car.LoadAsync();
		await Layers.LoadSwitchesAsync();
	}

	/// <inheritdoc />
	protected override void OnInitialized()
	{
		Spot.Changed += OnStateChanged;
		Spot.Nearest.Changed += OnStateChanged;
		Car.Changed += OnStateChanged;
		Layers.Changed += OnStateChanged;
		Spot.ViewRequested += OnViewRequested;
		Spot.FitRequested += OnFitRequested;
		Spot.LocationFailed += OnLocationFailed;
		Car.ShowRequested += OnCarShowRequested;
		Layers.LayerUpdated += OnLayerUpdated;
		Card.CardSaved += OnCardSaved;
		Welcome.AddCardRequested += OnAddCardRequested;
		Permission.ForegroundGranted += OnForegroundGranted;
	}
	#endregion

	#region Event Handlers
	/// <summary>
	/// Starts everything that needs the map once it exists: the layers, the markers, the tracking and the welcome dialog.
	/// </summary>
	/// <returns>A task that completes when the start finished.</returns>
	private async Task OnMapReadyAsync()
	{
		mapReady = true;
		await map!.Script.ObserveSheetAsync(sheet);
		Layers.Start();
		await SyncMapAsync();
		await Spot.StartTrackingAsync();
		await Welcome.StartAsync();
		await Permission.RefreshAsync();
	}

	/// <summary>
	/// Handles a tap on the map: closes the layers popover and drops the pin.
	/// </summary>
	/// <param name="point">The place that was tapped.</param>
	/// <returns>A task that completes when the spot was checked.</returns>
	private Task OnMapClickAsync(GeoPoint point)
	{
		layersOpen = false;
		return Spot.HandleMapClickAsync(point);
	}

	/// <summary>
	/// Handles the end of a drag of the pin.
	/// </summary>
	/// <param name="point">The place the pin was dropped on.</param>
	/// <returns>A task that completes when the spot was checked.</returns>
	private Task OnPinMovedAsync(GeoPoint point) => Spot.HandlePinMovedAsync(point);

	/// <summary>
	/// Passes a move of the map to the layers.
	/// </summary>
	/// <param name="change">The new area and zoom level.</param>
	private void OnViewChanged(MapViewChange change) => Layers.HandleViewChanged(change.Bounds, change.Zoom);

	/// <summary>
	/// Passes a popup button to the layers, which open the link.
	/// </summary>
	/// <param name="action">The button that was pressed.</param>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	private Task OnPopupActionAsync(MapPopupAction action) => Layers.HandlePopupActionAsync(action.Kind, action.Id);

	/// <summary>
	/// Handles the layers button: opens or closes the popover, and collapses the sheet to make room when it opens.
	/// </summary>
	private void OnToggleLayers()
	{
		layersOpen = !layersOpen;
		sheetCompact |= layersOpen;
	}

	/// <summary>
	/// Handles the close button of the layers popover.
	/// </summary>
	private void OnCloseLayers() => layersOpen = false;

	/// <summary>
	/// Handles the car button.
	/// </summary>
	private void OnOpenCar() => Car.Open();

	/// <summary>
	/// Handles the button that follows the position.
	/// </summary>
	/// <returns>A task that completes when the tracking changed.</returns>
	private Task OnToggleTrackingAsync() => Spot.ToggleTrackingAsync();

	/// <summary>
	/// Handles "Find free parking": expands the sheet and searches.
	/// </summary>
	/// <returns>A task that completes when the search ended.</returns>
	private Task OnFindAsync()
	{
		sheetCompact = false;
		return Spot.FindFreeParkingAsync();
	}

	/// <summary>
	/// Handles the handle of the bottom sheet.
	/// </summary>
	private void OnToggleSheet() => sheetCompact = !sheetCompact;
	#endregion

	#region Private Helpers
	/// <summary>
	/// Draws what changed on the map and renders the page again.
	/// </summary>
	/// <param name="sender">The view model that changed.</param>
	/// <param name="e">Not used.</param>
	private void OnStateChanged(object? sender, EventArgs e)
	{
		_ = InvokeAsync(async () =>
		{
			await SyncMapAsync();
			StateHasChanged();
		});
	}

	/// <summary>
	/// Moves the map as a view model asked.
	/// </summary>
	/// <param name="sender">The spot check.</param>
	/// <param name="e">The request.</param>
	private void OnViewRequested(object? sender, ValueEventArgs<MapViewRequest> e)
	{
		var request = e.Value;
		_ = InvokeAsync(() => mapReady ? ShowViewAsync(request) : Task.CompletedTask);
	}

	/// <summary>
	/// Zooms the map to two places as the spot check asked.
	/// </summary>
	/// <param name="sender">The spot check.</param>
	/// <param name="e">The request.</param>
	private void OnFitRequested(object? sender, ValueEventArgs<MapFitRequest> e)
	{
		var request = e.Value;
		_ = InvokeAsync(() => mapReady ? map!.Script.FitBoundsAsync(request.First, request.Second).AsTask() : Task.CompletedTask);
	}

	/// <summary>
	/// Shows the car on the map with its popup open, as the car dialog asked.
	/// </summary>
	/// <param name="sender">The car dialog.</param>
	/// <param name="e">The request.</param>
	private void OnCarShowRequested(object? sender, ValueEventArgs<MapViewRequest> e)
	{
		var request = e.Value;
		_ = InvokeAsync(async () =>
		{
			if (!mapReady)
			{
				return;
			}

			await ShowViewAsync(request);
			await map!.Script.SetCarAsync(request.Center, Car.PopupHtml);
			await map.Script.OpenCarPopupAsync();
		});
	}

	/// <summary>
	/// Draws a layer the layers view model published.
	/// </summary>
	/// <param name="sender">The layers view model.</param>
	/// <param name="e">The layer and its content.</param>
	private void OnLayerUpdated(object? sender, MapLayerUpdateEventArgs e)
	{
		_ = InvokeAsync(async () =>
		{
			if (!mapReady)
			{
				return;
			}

			await map!.Script.DrawSurfacesAsync(e.Layer, e.Surfaces);
			await map.Script.DrawPointsAsync(e.Layer + ":points", e.Points);
		});
	}

	/// <summary>
	/// Checks the spot and the layers again after a card was saved, and starts following the user after the first card.
	/// </summary>
	/// <param name="sender">The card view model.</param>
	/// <param name="e">Whether it was the first card.</param>
	private void OnCardSaved(object? sender, ValueEventArgs<bool> e)
	{
		var first = e.Value;
		_ = InvokeAsync(async () =>
		{
			await Spot.RecheckAsync();
			await Layers.RefreshAsync();
			if (first && !Spot.IsTracking)
			{
				await Spot.StartTrackingAsync();
			}
		});
	}

	/// <summary>
	/// Opens the card dialog after the welcome dialog.
	/// </summary>
	/// <param name="sender">The welcome view model.</param>
	/// <param name="e">Not used.</param>
	private void OnAddCardRequested(object? sender, EventArgs e)
	{
		_ = InvokeAsync(() => Card.OpenAsync());
	}

	/// <summary>
	/// Refreshes the permission banner after the position could not be read.
	/// </summary>
	/// <param name="sender">The spot check.</param>
	/// <param name="e">Not used.</param>
	private void OnLocationFailed(object? sender, EventArgs e)
	{
		_ = InvokeAsync(() => Permission.RefreshAsync());
	}

	/// <summary>
	/// Starts following the user once the foreground permission was given.
	/// </summary>
	/// <param name="sender">The permission view model.</param>
	/// <param name="e">Not used.</param>
	private void OnForegroundGranted(object? sender, EventArgs e)
	{
		_ = InvokeAsync(() => Spot.StartTrackingAsync());
	}

	/// <summary>
	/// Moves the map as a request says: to an exact zoom level, or to at least a zoom level.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <returns>A task that completes when the map was moved.</returns>
	private async Task ShowViewAsync(MapViewRequest request)
	{
		var script = map!.Script;
		if (request.OnlyZoomIn && request.Zoom is { } minimumZoom)
		{
			await script.SetViewAtLeastAsync(request.Center, minimumZoom);
			return;
		}

		await script.SetViewAsync(request.Center, request.Zoom);
	}

	/// <summary>
	/// Brings the markers and the zone image on the map in line with the view models, drawing only what changed.
	/// </summary>
	/// <returns>A task that completes when the map was updated.</returns>
	private async Task SyncMapAsync()
	{
		if (!mapReady)
		{
			return;
		}

		var script = map!.Script;
		if (!ReferenceEquals(drawnMe, Spot.Me))
		{
			drawnMe = Spot.Me;
			await script.SetMeAsync(drawnMe?.Fix.Point, drawnMe?.Fix.AccuracyMetres ?? 0);
		}

		if (drawnPin != Spot.Pin)
		{
			drawnPin = Spot.Pin;
			await script.SetPinAsync(drawnPin);
		}

		var target = Spot.Nearest.State == NearestSpotState.Found ? Spot.Nearest.Best?.Point : null;
		if (drawnTarget != target)
		{
			drawnTarget = target;
			await script.SetTargetAsync(target);
		}

		var car = Car.Car?.Point;
		if (drawnCar != car)
		{
			drawnCar = car;
			await script.SetCarAsync(car, Car.PopupHtml);
		}

		if (drawnZoneImage != Layers.ZoneImageVisible)
		{
			drawnZoneImage = Layers.ZoneImageVisible;
			await script.ShowZonesWmsAsync(drawnZoneImage);
		}
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		Spot.Changed -= OnStateChanged;
		Spot.Nearest.Changed -= OnStateChanged;
		Car.Changed -= OnStateChanged;
		Layers.Changed -= OnStateChanged;
		Spot.ViewRequested -= OnViewRequested;
		Spot.FitRequested -= OnFitRequested;
		Spot.LocationFailed -= OnLocationFailed;
		Car.ShowRequested -= OnCarShowRequested;
		Layers.LayerUpdated -= OnLayerUpdated;
		Card.CardSaved -= OnCardSaved;
		Welcome.AddCardRequested -= OnAddCardRequested;
		Permission.ForegroundGranted -= OnForegroundGranted;
		GC.SuppressFinalize(this);
	}
	#endregion
}
