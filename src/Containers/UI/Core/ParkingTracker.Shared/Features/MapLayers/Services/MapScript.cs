using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;

namespace ParkingTracker.Shared.Features.MapLayers.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The typed face of <c>wwwroot/js/map.js</c>, the ES module that owns the Leaflet map (docs/architecture.md section
///              3.3). It imports the module on <see cref="InitAsync{TCallbacks}"/>, forwards every call with the compact payloads of
///              the view models, does nothing until the map exists, and releases the module and the .NET callback reference on disposal.
/// </summary>
public sealed class MapScript : IAsyncDisposable, IDisposable
{
	#region Constants
	/// <summary>
	/// The address of the map module inside the Razor class library.
	/// </summary>
	public const string ModulePath = "./_content/ParkingTracker.Shared/js/map.js";
	#endregion

	#region Fields
	/// <summary>
	/// The JavaScript runtime that imports the module.
	/// </summary>
	private readonly IJSRuntime runtime;

	/// <summary>
	/// The imported module, or <see langword="null"/> before <see cref="InitAsync{TCallbacks}"/> and after disposal.
	/// </summary>
	private IJSObjectReference? module;

	/// <summary>
	/// The reference that lets the script call back into .NET. Its type depends on the caller of <see cref="InitAsync{TCallbacks}"/>, so it is held as a disposable.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "The concrete type is the generic argument of InitAsync, which the class does not know.")]
	private IDisposable? callbacks;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MapScript"/> class.
	/// </summary>
	/// <param name="runtime">The JavaScript runtime.</param>
	public MapScript(IJSRuntime runtime)
	{
		this.runtime = runtime;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Imports the module and creates the map in an element.
	/// </summary>
	/// <typeparam name="TCallbacks">The type that holds the <c>[JSInvokable]</c> methods the script calls back.</typeparam>
	/// <param name="element">The element that holds the map.</param>
	/// <param name="center">The place the map starts at.</param>
	/// <param name="zoom">The zoom level the map starts at.</param>
	/// <param name="target">The object that receives <c>OnMapClick</c>, <c>OnPinMoved</c>, <c>OnViewChanged</c> and <c>OnPopupAction</c>.</param>
	/// <returns>A task that completes when the map exists.</returns>
	public async Task InitAsync<TCallbacks>(ElementReference element, GeoPoint center, int zoom, TCallbacks target)
		where TCallbacks : class
	{
		module = await runtime.InvokeAsync<IJSObjectReference>("import", ModulePath).ConfigureAwait(false);
		var reference = DotNetObjectReference.Create(target);
		callbacks = reference;
		await module.InvokeVoidAsync("init", element, new { lat = center.Latitude, lon = center.Longitude, zoom }, reference).ConfigureAwait(false);
	}

	/// <summary>
	/// Moves the map to a place at an exact zoom level.
	/// </summary>
	/// <param name="center">The place to centre on.</param>
	/// <param name="zoom">The zoom level, or <see langword="null"/> to keep the current one.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask SetViewAsync(GeoPoint center, int? zoom) => Call("setView", center.Latitude, center.Longitude, zoom);

	/// <summary>
	/// Moves the map to a place and zooms in to at least a level; a map that is already closer keeps its zoom level.
	/// </summary>
	/// <param name="center">The place to centre on.</param>
	/// <param name="minimumZoom">The closest zoom level that is kept at least.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask SetViewAtLeastAsync(GeoPoint center, int minimumZoom) => Call("setViewAtLeast", center.Latitude, center.Longitude, minimumZoom);

	/// <summary>
	/// Zooms the map so that two places are visible.
	/// </summary>
	/// <param name="first">The first place.</param>
	/// <param name="second">The second place.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask FitBoundsAsync(GeoPoint first, GeoPoint second) => Call("fitBounds", first.Latitude, first.Longitude, second.Latitude, second.Longitude);

	/// <summary>
	/// Shows or removes the position of the user.
	/// </summary>
	/// <param name="position">The position, or <see langword="null"/> to remove the marker.</param>
	/// <param name="accuracyMetres">The radius of the accuracy circle in metres.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask SetMeAsync(GeoPoint? position, double accuracyMetres) => Call("setMe", position?.Latitude, position?.Longitude, accuracyMetres);

	/// <summary>
	/// Shows or removes the pin.
	/// </summary>
	/// <param name="position">The place of the pin, or <see langword="null"/> to remove it.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask SetPinAsync(GeoPoint? position) => Call("setPin", position?.Latitude, position?.Longitude);

	/// <summary>
	/// Shows or removes the parked car.
	/// </summary>
	/// <param name="position">Where the car is, or <see langword="null"/> to remove the marker.</param>
	/// <param name="popupHtml">The popup text, with the HTML escaped.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask SetCarAsync(GeoPoint? position, string popupHtml) => Call("setCar", position?.Latitude, position?.Longitude, popupHtml);

	/// <summary>
	/// Opens the popup of the parked car, when the car is shown.
	/// </summary>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask OpenCarPopupAsync() => Call("openCarPopup");

	/// <summary>
	/// Shows or removes the marker of the nearest free spot.
	/// </summary>
	/// <param name="position">Where the spot is, or <see langword="null"/> to remove the marker.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask SetTargetAsync(GeoPoint? position) => Call("setTarget", position?.Latitude, position?.Longitude);

	/// <summary>
	/// Replaces the areas of a layer.
	/// </summary>
	/// <param name="layer">The name of the layer.</param>
	/// <param name="items">The areas to draw; empty to clear.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask DrawSurfacesAsync(string layer, IReadOnlyList<MapSurface> items) => Call("drawSurfaces", layer, items);

	/// <summary>
	/// Replaces the markers of a layer.
	/// </summary>
	/// <param name="layer">The name of the layer.</param>
	/// <param name="items">The markers to draw; empty to clear.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask DrawPointsAsync(string layer, IReadOnlyList<MapPoint> items) => Call("drawPoints", layer, items);

	/// <summary>
	/// Shows or hides the official zone colours drawn by the parking.brussels map server.
	/// </summary>
	/// <param name="on">Whether the image is shown.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask ShowZonesWmsAsync(bool on) => Call("showZonesWms", on);

	/// <summary>
	/// Empties a layer.
	/// </summary>
	/// <param name="layer">The name of the layer.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask ClearAsync(string layer) => Call("clear", layer);

	/// <summary>
	/// Keeps the height of the bottom sheet in a CSS variable, so that the map controls sit above it.
	/// </summary>
	/// <param name="sheet">The element of the bottom sheet.</param>
	/// <returns>A task that completes when the call was made.</returns>
	public ValueTask ObserveSheetAsync(ElementReference sheet) => Call("observeSheet", sheet);
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	/// <remarks>
	/// Only drops the references, because the module cannot be told to remove the map without waiting for the script; the container
	/// that owns this object calls <see cref="DisposeAsync"/> when it can, and the map component does so itself.
	/// </remarks>
	public void Dispose()
	{
		callbacks?.Dispose();
		callbacks = null;
		module = null;
	}
	#endregion

	#region IAsyncDisposable Implementation
	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		var current = module;
		module = null;
		callbacks?.Dispose();
		callbacks = null;
		if (current is null)
		{
			return;
		}

		await current.InvokeVoidAsync("dispose").ConfigureAwait(false);
		await current.DisposeAsync().ConfigureAwait(false);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Calls a function of the module, when the map exists.
	/// </summary>
	/// <param name="function">The name of the exported function.</param>
	/// <param name="arguments">The arguments of the function.</param>
	/// <returns>A task that completes when the call was made; at once when the map does not exist.</returns>
	private ValueTask Call(string function, params object?[] arguments)
	{
		return module is null ? ValueTask.CompletedTask : module.InvokeVoidAsync(function, arguments);
	}
	#endregion
}
