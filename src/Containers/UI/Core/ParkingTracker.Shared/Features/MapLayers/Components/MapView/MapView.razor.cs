using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Shared.Features.MapLayers.Services;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Features.MapLayers.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The map: an element that the Leaflet script fills, a <see cref="MapScript"/> to draw on it, and the four
///              <c>[JSInvokable]</c> callbacks of docs/architecture.md section 3.3 (<c>OnMapClick</c>, <c>OnPinMoved</c>,
///              <c>OnViewChanged</c>, <c>OnPopupAction</c>), which it turns into component callbacks. It disposes the script
///              module and the .NET reference when it goes away.
/// </summary>
public sealed partial class MapView : ComponentBase, IAsyncDisposable
{
	#region Constants
	/// <summary>
	/// The latitude the map starts at: the middle of the Louise area of Brussels.
	/// </summary>
	private const double StartLatitude = 50.8285;

	/// <summary>
	/// The longitude the map starts at.
	/// </summary>
	private const double StartLongitude = 4.3640;

	/// <summary>
	/// The zoom level the map starts at.
	/// </summary>
	private const int StartZoom = 16;
	#endregion

	#region Injected Services
	/// <summary>
	/// Gets or sets the script wrapper this map draws with.
	/// </summary>
	[Inject]
	public MapScript Script { get; set; } = null!;
	#endregion

	#region Parameters
	/// <summary>
	/// Gets or sets the callback raised after the map was created.
	/// </summary>
	[Parameter]
	public EventCallback OnReady { get; set; }

	/// <summary>
	/// Gets or sets the callback raised when the user taps the map.
	/// </summary>
	[Parameter]
	public EventCallback<GeoPoint> OnMapClick { get; set; }

	/// <summary>
	/// Gets or sets the callback raised when the user dropped the pin on a new place.
	/// </summary>
	[Parameter]
	public EventCallback<GeoPoint> OnPinMoved { get; set; }

	/// <summary>
	/// Gets or sets the callback raised when the map moved or zoomed.
	/// </summary>
	[Parameter]
	public EventCallback<MapViewChange> OnViewChanged { get; set; }

	/// <summary>
	/// Gets or sets the callback raised when the user pressed a button in a popup.
	/// </summary>
	[Parameter]
	public EventCallback<MapPopupAction> OnPopupAction { get; set; }
	#endregion

	#region Fields
	/// <summary>
	/// The element the script fills with the map.
	/// </summary>
	private ElementReference element;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!firstRender)
		{
			return;
		}

		await Script.InitAsync(element, new GeoPoint(StartLatitude, StartLongitude), StartZoom, this);
		await OnReady.InvokeAsync();
	}
	#endregion

	#region Event Handlers
	/// <summary>
	/// Receives a tap on the map from the script.
	/// </summary>
	/// <param name="latitude">The latitude that was tapped.</param>
	/// <param name="longitude">The longitude that was tapped.</param>
	/// <returns>A task that completes when the callback ran.</returns>
	[JSInvokable("OnMapClick")]
	public Task OnMapClickAsync(double latitude, double longitude) => OnMapClick.InvokeAsync(new GeoPoint(latitude, longitude));

	/// <summary>
	/// Receives the end of a drag of the pin from the script.
	/// </summary>
	/// <param name="latitude">The latitude of the pin.</param>
	/// <param name="longitude">The longitude of the pin.</param>
	/// <returns>A task that completes when the callback ran.</returns>
	[JSInvokable("OnPinMoved")]
	public Task OnPinMovedAsync(double latitude, double longitude) => OnPinMoved.InvokeAsync(new GeoPoint(latitude, longitude));

	/// <summary>
	/// Receives a move of the map from the script.
	/// </summary>
	/// <param name="south">The southern edge.</param>
	/// <param name="west">The western edge.</param>
	/// <param name="north">The northern edge.</param>
	/// <param name="east">The eastern edge.</param>
	/// <param name="zoom">The zoom level.</param>
	/// <returns>A task that completes when the callback ran.</returns>
	[JSInvokable("OnViewChanged")]
	public Task OnViewChangedAsync(double south, double west, double north, double east, double zoom)
	{
		return OnViewChanged.InvokeAsync(new MapViewChange(new MapBounds(south, west, north, east), zoom));
	}

	/// <summary>
	/// Receives a popup button press from the script.
	/// </summary>
	/// <param name="kind">The action name of the button.</param>
	/// <param name="id">The value of the button.</param>
	/// <returns>A task that completes when the callback ran.</returns>
	[JSInvokable("OnPopupAction")]
	public Task OnPopupActionAsync(string kind, string id) => OnPopupAction.InvokeAsync(new MapPopupAction(kind, id));
	#endregion

	#region IAsyncDisposable Implementation
	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		await Script.DisposeAsync();
		GC.SuppressFinalize(this);
	}
	#endregion
}
