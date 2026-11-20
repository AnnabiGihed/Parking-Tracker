using Bunit;
using NSubstitute;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Shared.Features.MapLayers.Components;
using ParkingTracker.Shared.Features.MapLayers.Services;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Tests.Features.MapLayers.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for <see cref="MapView"/> and <see cref="MapScript"/> with the JavaScript
///              interop in strict mode: the map module is imported and initialised, the four callbacks reach the component callbacks,
///              calls before the map exists do nothing, and disposing the component disposes the module and the .NET reference.
/// </summary>
public sealed class MapViewTests : UiTestContext
{
	#region Map View Tests
	/// <summary>
	/// Verifies that the module is imported and the map created in the element, and that the ready callback runs.
	/// </summary>
	[Fact]
	public void Render_FirstRender_ShouldImportTheModuleAndInitialiseTheMap()
	{
		var module = JSInterop.SetupModule(MapScript.ModulePath);
		module.SetupVoid("init", _ => true).SetVoidResult();
		var ready = false;

		Render<MapView>(parameters => parameters.Add(map => map.OnReady, () => ready = true));

		module.VerifyInvoke("init");
		ready.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that disposing the component calls <c>dispose</c> of the module, which removes the map and its references.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Dispose_Component_ShouldDisposeTheModuleAndTheMap()
	{
		var module = JSInterop.SetupModule(MapScript.ModulePath);
		module.SetupVoid("init", _ => true).SetVoidResult();
		module.SetupVoid("dispose").SetVoidResult();
		Render<MapView>();

		await DisposeComponentsAsync();

		module.VerifyInvoke("dispose");
	}

	/// <summary>
	/// Verifies that the five callbacks of the script reach the component callbacks with their values.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Callbacks_FromTheScript_ShouldReachTheComponentCallbacks()
	{
		var module = JSInterop.SetupModule(MapScript.ModulePath);
		module.SetupVoid("init", _ => true).SetVoidResult();
		GeoPoint? clicked = null;
		GeoPoint? moved = null;
		GeoPoint? candidateMoved = null;
		MapViewChange? changed = null;
		MapPopupAction? action = null;
		var cut = Render<MapView>(parameters => parameters
			.Add(map => map.OnMapClick, point => clicked = point)
			.Add(map => map.OnPinMoved, point => moved = point)
			.Add(map => map.OnCandidateMoved, point => candidateMoved = point)
			.Add(map => map.OnViewChanged, change => changed = change)
			.Add(map => map.OnPopupAction, popup => action = popup));

		await cut.InvokeAsync(() => cut.Instance.OnMapClickAsync(50.8, 4.3));
		await cut.InvokeAsync(() => cut.Instance.OnPinMovedAsync(50.81, 4.31));
		await cut.InvokeAsync(() => cut.Instance.OnCandidateMovedAsync(50.82, 4.32));
		await cut.InvokeAsync(() => cut.Instance.OnViewChangedAsync(50.8, 4.3, 50.9, 4.4, 16));
		await cut.InvokeAsync(() => cut.Instance.OnPopupActionAsync("waze", "50.8,4.3"));

		clicked.Should().Be(new GeoPoint(50.8, 4.3));
		moved.Should().Be(new GeoPoint(50.81, 4.31));
		candidateMoved.Should().Be(new GeoPoint(50.82, 4.32));
		changed.Should().Be(new MapViewChange(new MapBounds(50.8, 4.3, 50.9, 4.4), 16));
		action.Should().Be(new MapPopupAction("waze", "50.8,4.3"));
	}
	#endregion

	#region Map Script Tests
	/// <summary>
	/// Verifies that calls made before the map exists do nothing, so that a view model event that arrives early cannot break the page.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Script_BeforeInit_ShouldDoNothing()
	{
		var script = new MapScript(JSInterop.JSRuntime);

		await script.SetViewAtLeastAsync(Here, 17);
		await script.DrawPointsAsync("spaces", []);
		await script.DisposeAsync();

		JSInterop.Invocations.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that the script forwards the calls of docs/architecture.md section 3.3 to the module with the values given.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Script_AfterInit_ShouldForwardTheCallsToTheModule()
	{
		var module = JSInterop.SetupModule(MapScript.ModulePath);
		module.SetupVoid(_ => true).SetVoidResult();
		var script = new MapScript(JSInterop.JSRuntime);
		await script.InitAsync(default, Here, 16, new object());

		await script.SetViewAsync(Here, null);
		await script.SetViewAtLeastAsync(Here, 17);
		await script.FitBoundsAsync(Here, new GeoPoint(50.83, 4.37));
		await script.SetMeAsync(Here, 12);
		await script.SetPinAsync(null);
		await script.SetCandidateAsync(Here);
		await script.SetCandidateAsync(null);
		await script.InvalidateSizeAsync();
		await script.SetCarAsync(Here, "<b>My car</b>");
		await script.OpenCarPopupAsync();
		await script.SetTargetAsync(Here);
		await script.DrawSurfacesAsync("streets", [new MapSurface("covered", [], null)]);
		await script.DrawPointsAsync("spaces", [new MapPoint(50.825, 4.3606, "space", null, null)]);
		await script.ShowZonesWmsAsync(true);
		await script.ClearAsync("works");

		module.Invocations.Select(invocation => invocation.Identifier).Should().Equal("init", "setView", "setViewAtLeast", "fitBounds", "setMe", "setPin", "setCandidate", "setCandidate", "invalidateSize", "setCar", "openCarPopup", "setTarget", "drawSurfaces", "drawPoints", "showZonesWms", "clear");
		module.Invocations["setView"].Single().Arguments.Should().Equal(50.825, 4.3606, null);
		module.Invocations["setViewAtLeast"].Single().Arguments.Should().Equal(50.825, 4.3606, 17);
		module.Invocations["setCandidate"][0].Arguments.Should().Equal(50.825, 4.3606);
		module.Invocations["setCandidate"][1].Arguments.Should().Equal(new object?[] { null, null });
		module.Invocations["drawSurfaces"].Single().Arguments[0].Should().Be("streets");
	}
	#endregion
}
