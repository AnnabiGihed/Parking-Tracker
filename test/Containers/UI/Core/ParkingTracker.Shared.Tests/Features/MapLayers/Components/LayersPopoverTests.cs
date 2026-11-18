using Bunit;
using NSubstitute;
using Radzen.Blazor;
using ParkingTracker.Application.Features.MapLayers.Queries.GetMapLayers;
using ParkingTracker.Application.Features.SpotDecisions.Queries.GetAreaStatuses;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Shared.Features.MapLayers.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Shared.Tests.Features.MapLayers.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the layers popover, built from Radzen components: one switch per layer that shows the saved
///              layers and saves a change through the view model, the close button, the legend, and the state line with the offline
///              text and the failed layers.
/// </summary>
public sealed class LayersPopoverTests : UiTestContext
{
	#region Constants
	/// <summary>
	/// An area around the Louise area, at street level.
	/// </summary>
	private static readonly MapBounds Bounds = new(50.82, 4.35, 50.83, 4.37);
	#endregion

	#region Switch Tests
	/// <summary>
	/// Verifies that a switch of the popover is saved through the settings store and that the popover is shown when open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Toggle_CarParksSwitch_ShouldSaveTheSwitches()
	{
		var cut = Render<LayersPopover>(parameters => parameters.Add(popover => popover.IsOpen, true));
		cut.Find("#layersPanel").ClassList.Should().NotContain("hidden");

		await cut.Find("[data-layer=carparks]").ClickAsync();

		_ = Settings.Received(1).SaveLayersAsync(new LayerSettings(true, true, true, true, false));
		Resolve<MapLayersViewModel>().Switches.CarParks.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that the switches show the saved layers.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_SavedLayers_ShouldSetEachSwitch()
	{
		Settings.GetLayersAsync().Returns(new LayerSettings(false, true, false, true, true));
		await Resolve<MapLayersViewModel>().LoadSwitchesAsync();

		var cut = Render<LayersPopover>();

		cut.FindComponents<RadzenSwitch>().Select(toggle => toggle.Instance.Value).Should().Equal(false, true, false, true, true);
	}

	/// <summary>
	/// Verifies that turning a switch off saves the layer as off.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Toggle_StreetsSwitchOn_ShouldSaveItOff()
	{
		var cut = Render<LayersPopover>(parameters => parameters.Add(popover => popover.IsOpen, true));

		await cut.Find("[data-layer=streets]").ClickAsync();

		_ = Settings.Received(1).SaveLayersAsync(new LayerSettings(false, true, true, false, false));
		cut.FindComponents<RadzenSwitch>()[0].Instance.Value.Should().BeFalse();
	}
	#endregion

	#region Layout Tests
	/// <summary>
	/// Verifies that the popover lists the five layers and the legend, and is hidden until opened.
	/// </summary>
	[Fact]
	public void Render_Closed_ShouldListFiveLayersAndTheLegendHidden()
	{
		var cut = Render<LayersPopover>();

		cut.FindAll(".switch").Should().HaveCount(5);
		cut.FindComponents<RadzenSwitch>().Should().HaveCount(5);
		cut.FindAll(".legend-row span").Should().HaveCount(7);
		cut.Find("#layersPanel").ClassList.Should().Contain("hidden");
	}

	/// <summary>
	/// Verifies that the close button raises the close callback.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Close_Button_ShouldRaiseTheCloseCallback()
	{
		var closed = false;
		var cut = Render<LayersPopover>(parameters => parameters
			.Add(popover => popover.IsOpen, true)
			.Add(popover => popover.OnClose, () => closed = true));

		await cut.Find("button[aria-label=Close]").ClickAsync();

		closed.Should().BeTrue();
	}
	#endregion

	#region State Line Tests
	/// <summary>
	/// Verifies that offline the state line says that no live layers are shown.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_Offline_ShouldSayOfflineInTheStateLine()
	{
		Network.IsOnline.Returns(false);
		var cut = Render<LayersPopover>(parameters => parameters.Add(popover => popover.IsOpen, true));

		await RefreshAsync(cut);

		cut.Find("#layerState").TextContent.Should().Be("offline: no live layers");
	}

	/// <summary>
	/// Verifies that zoomed out the state line tells to zoom in.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_ZoomedOut_ShouldHintToZoomIn()
	{
		var cut = Render<LayersPopover>(parameters => parameters.Add(popover => popover.IsOpen, true));

		await RefreshAsync(cut, zoom: 14);

		cut.Find("#layerState").TextContent.Should().Contain("Zoom in to see where your card works");
	}

	/// <summary>
	/// Verifies that a layer that failed is named in the state line.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_LayerFails_ShouldNameItInTheStateLine()
	{
		var cut = Render<LayersPopover>(parameters => parameters.Add(popover => popover.IsOpen, true));

		await RefreshAsync(cut, failures: new Dictionary<string, string>(StringComparer.Ordinal) { ["works"] = "roadworks HTTP 500" });

		cut.Find("#layerState").TextContent.Should().Be("some layers failed: works: roadworks HTTP 500");
	}
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Gives the view model a saved card and a map view, then loads the layers.
	/// </summary>
	/// <param name="cut">The rendered popover.</param>
	/// <param name="zoom">The zoom of the map.</param>
	/// <param name="failures">The layers the answer reports as failed, with their messages.</param>
	/// <returns>A task that completes when the layers were loaded.</returns>
	private async Task RefreshAsync(IRenderedComponent<LayersPopover> cut, double zoom = 16, Dictionary<string, string>? failures = null)
	{
		Settings.GetCardAsync().Returns(new ParkingCard("etterbeek", null, new CardPeriod(null, null), [], [], []));
		Sender.Send(Arg.Any<GetAreaStatusesQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<SurfaceStatus>>([]));
		Sender.Send(Arg.Any<GetMapLayersQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new GetMapLayersResponse([], [], [], [], [], failures ?? [])));
		var layers = Resolve<MapLayersViewModel>();
		layers.HandleViewChanged(Bounds, zoom);

		await cut.InvokeAsync(layers.RefreshAsync).ConfigureAwait(false);
	}
	#endregion
}
