using NSubstitute;
using ParkingTracker.Application.Features.MapLayers.Queries.GetMapLayers;
using ParkingTracker.Application.Features.SpotDecisions.Queries.GetAreaStatuses;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="MapLayersViewModel"/>: the 700 ms debounce on the fake clock, the layers asked at each zoom,
///              the zone image when zoomed out, the offline state, the failing layer named in the status line, the saved switches and
///              the checks on what a popup button may open.
/// </summary>
public sealed class MapLayersViewModelTests
{
	#region Static Instances
	/// <summary>
	/// An area around the Louise area.
	/// </summary>
	private static readonly MapBounds Bounds = new(50.82, 4.35, 50.83, 4.37);
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Builds a fixture with a saved card and empty answers for both layer queries.
	/// </summary>
	/// <param name="failures">The failures the map-layers answer reports.</param>
	/// <returns>The fixture.</returns>
	private static UiFixture FixtureWithCard(Dictionary<string, string>? failures = null)
	{
		var ui = new UiFixture();
		ui.Settings.GetCardAsync().Returns(new ParkingCard("etterbeek", null, new CardPeriod(null, null), [], [], []));
		ui.Sender.Send(Arg.Any<GetAreaStatusesQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<SurfaceStatus>>([]));
		ui.Sender.Send(Arg.Any<GetMapLayersQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new GetMapLayersResponse([], [], [], [], [], failures ?? [])));
		return ui;
	}
	#endregion

	#region Debounce Tests
	/// <summary>
	/// Verifies that the layers are loaded once, 700 ms after the last move of the map.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task HandleViewChanged_RepeatedMoves_ShouldLoadOnceAfterSevenHundredMilliseconds()
	{
		using var ui = FixtureWithCard();

		ui.Layers.HandleViewChanged(Bounds, 16);
		ui.Clock.Advance(TimeSpan.FromMilliseconds(500));
		ui.Layers.HandleViewChanged(Bounds, 16);
		ui.Clock.Advance(TimeSpan.FromMilliseconds(699));
		await ui.Sender.DidNotReceive().Send(Arg.Any<GetMapLayersQuery>(), Arg.Any<CancellationToken>());
		ui.Clock.Advance(TimeSpan.FromMilliseconds(1));

		await ui.Sender.Received(1).Send(Arg.Any<GetMapLayersQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that started layers are loaded again every two minutes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_Timer_ShouldLoadAgainEveryTwoMinutes()
	{
		using var ui = FixtureWithCard();
		ui.Layers.Start();
		ui.Layers.HandleViewChanged(Bounds, 16);
		ui.Clock.Advance(TimeSpan.FromMilliseconds(700));

		ui.Clock.Advance(TimeSpan.FromSeconds(120));

		await ui.Sender.Received(2).Send(Arg.Any<GetMapLayersQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that nothing is loaded without a saved card.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_NoCard_ShouldLoadNothing()
	{
		using var ui = new UiFixture();
		ui.Layers.HandleViewChanged(Bounds, 16);

		await ui.Layers.RefreshAsync();

		await ui.Sender.DidNotReceive().Send(Arg.Any<GetMapLayersQuery>(), Arg.Any<CancellationToken>());
	}
	#endregion

	#region Zoom Tests
	/// <summary>
	/// Verifies that zoomed out only the zone image is shown and no street data is asked for.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_ZoomedOut_ShouldShowTheZoneImageAndAskForNoStreets()
	{
		using var ui = FixtureWithCard();
		ui.Layers.HandleViewChanged(Bounds, 14);

		await ui.Layers.RefreshAsync();

		ui.Layers.ZoneImageVisible.Should().BeTrue();
		ui.Layers.StatusText.Should().Be("Official zone colours (red, grey, green, blue…). Zoom in to see where your card works.");
		await ui.Sender.DidNotReceive().Send(Arg.Any<GetAreaStatusesQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that house numbers are asked for from zoom 16 and parking spaces from zoom 17.
	/// </summary>
	/// <param name="zoom">The zoom level.</param>
	/// <param name="houseNumbers">Whether house numbers are asked for.</param>
	/// <param name="spaces">Whether the parking spaces layer is asked for.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(15, false, false)]
	[InlineData(16, true, false)]
	[InlineData(17, true, true)]
	public async Task Refresh_Zoom_ShouldAskForDetailFromItsMinimum(double zoom, bool houseNumbers, bool spaces)
	{
		using var ui = FixtureWithCard();
		ui.Layers.HandleViewChanged(Bounds, zoom);

		await ui.Layers.RefreshAsync();

		ui.Layers.ZoneImageVisible.Should().BeFalse();
		await ui.Sender.Received(1).Send(Arg.Is<GetAreaStatusesQuery>(q => q.WithAddresses == houseNumbers), Arg.Any<CancellationToken>());
		await ui.Sender.Received(1).Send(Arg.Is<GetMapLayersQuery>(q => q.Layers.Spaces == spaces && q.Layers.Streets), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the area is padded by a tenth on each side before it is asked for.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_Area_ShouldBePaddedByTenPercent()
	{
		using var ui = FixtureWithCard();
		ui.Layers.HandleViewChanged(new MapBounds(50.0, 4.0, 51.0, 5.0), 16);

		await ui.Layers.RefreshAsync();

		await ui.Sender.Received(1).Send(Arg.Is<GetAreaStatusesQuery>(q => Math.Abs(q.South - 49.9) < 1e-9 && Math.Abs(q.North - 51.1) < 1e-9 && Math.Abs(q.West - 3.9) < 1e-9 && Math.Abs(q.East - 5.1) < 1e-9), Arg.Any<CancellationToken>());
	}
	#endregion

	#region State Tests
	/// <summary>
	/// Verifies that offline every layer is cleared and the status line says so.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_Offline_ShouldClearEveryLayerAndSayOffline()
	{
		using var ui = FixtureWithCard();
		ui.Network.IsOnline.Returns(false);
		var cleared = new List<string>();
		ui.Layers.LayerUpdated += (_, e) => cleared.Add(e.Layer);
		ui.Layers.HandleViewChanged(Bounds, 16);

		await ui.Layers.RefreshAsync();

		ui.Layers.StatusText.Should().Be("offline: no live layers");
		ui.Layers.ZoneImageVisible.Should().BeFalse();
		cleared.Should().Contain(["streets", "areas", "spaces", "works", "carparks", "chargers"]);
		await ui.Sender.DidNotReceive().Send(Arg.Any<GetMapLayersQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a layer that failed is named in the status line while the others are published.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_LayerFails_ShouldNameItInTheStatusLine()
	{
		using var ui = FixtureWithCard(new Dictionary<string, string>(StringComparer.Ordinal) { ["works"] = "roadworks HTTP 500" });
		var published = new List<string>();
		ui.Layers.LayerUpdated += (_, e) => published.Add(e.Layer);
		ui.Layers.HandleViewChanged(Bounds, 16);

		await ui.Layers.RefreshAsync();

		ui.Layers.StatusText.Should().Be("some layers failed: works: roadworks HTTP 500");
		published.Should().Contain(["streets", "spaces", "works"]);
	}

	/// <summary>
	/// Verifies that a street layer that fails is named and cleared.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_StreetsFail_ShouldNameTheStreetLayer()
	{
		using var ui = FixtureWithCard();
		ui.Sender.Send(Arg.Any<GetAreaStatusesQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<SurfaceStatus>>(new Error("ParkingBrussels.Unavailable", "parking.brussels HTTP 500")));
		ui.Layers.HandleViewChanged(Bounds, 16);

		await ui.Layers.RefreshAsync();

		ui.Layers.StatusText.Should().Be("some layers failed: streets: parking.brussels HTTP 500");
	}

	/// <summary>
	/// Verifies that a switch is saved and the layers are loaded at once.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SetLayer_Switch_ShouldSaveItAndReload()
	{
		using var ui = FixtureWithCard();
		ui.Layers.HandleViewChanged(Bounds, 16);

		await ui.Layers.SetLayerAsync("carparks", true);

		_ = ui.Settings.Received(1).SaveLayersAsync(new LayerSettings(true, true, true, true, false));
		ui.Layers.Switches.CarParks.Should().BeTrue();
		await ui.Sender.Received(1).Send(Arg.Is<GetMapLayersQuery>(q => q.Layers.CarParks), Arg.Any<CancellationToken>());
	}
	#endregion

	#region Popup Action Tests
	/// <summary>
	/// Verifies that a popup button opens Waze, Google Maps or a phone number through the launcher.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task HandlePopupAction_ValidActions_ShouldOpenTheLinks()
	{
		using var ui = new UiFixture();

		await ui.Layers.HandlePopupActionAsync("waze", "50.825000,4.360600");
		await ui.Layers.HandlePopupActionAsync("gmaps", "50.825000,4.360600");
		await ui.Layers.HandlePopupActionAsync("link", "tel:+3225000000");
		await ui.Layers.HandlePopupActionAsync("link", "https://example.org/works");

		await ui.Launcher.Received(1).OpenAsync(new Uri("https://waze.com/ul?ll=50.825000,4.360600&navigate=yes"), Arg.Any<CancellationToken>());
		await ui.Launcher.Received(1).OpenAsync(new Uri("https://www.google.com/maps/dir/?api=1&destination=50.825000,4.360600&travelmode=driving"), Arg.Any<CancellationToken>());
		await ui.Launcher.Received(1).OpenAsync(new Uri("tel:+3225000000"), Arg.Any<CancellationToken>());
		await ui.Launcher.Received(1).OpenAsync(new Uri("https://example.org/works"), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that values from the page that are not a position or an allowed address, and unknown actions, are ignored.
	/// </summary>
	/// <param name="kind">The action name.</param>
	/// <param name="id">The value.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("waze", "not a position")]
	[InlineData("waze", "91,4")]
	[InlineData("link", "javascript:alert(1)")]
	[InlineData("link", "file:///etc/passwd")]
	[InlineData("link", "relative/path")]
	[InlineData("other", "50.8,4.3")]
	public async Task HandlePopupAction_InvalidValues_ShouldBeIgnored(string kind, string id)
	{
		using var ui = new UiFixture();

		await ui.Layers.HandlePopupActionAsync(kind, id);

		await ui.Launcher.DidNotReceiveWithAnyArgs().OpenAsync(default!, default);
	}
	#endregion
}
