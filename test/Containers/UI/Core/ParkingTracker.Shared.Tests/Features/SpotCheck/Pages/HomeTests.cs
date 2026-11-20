using Bunit;
using NSubstitute;
using Radzen.Blazor;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Shared.Features.MapLayers.Components;
using ParkingTracker.Shared.Features.MapLayers.Services;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.Shared.Features.SpotCheck.Pages;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.Shared.Tests.Features.SpotCheck.Pages;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the <see cref="Home"/> page: once the map exists the page starts following the user and opens the
///              welcome dialog, a tap on the map drops a pin and shows the answer, the page lays out the controls of <c>index.html</c>,
///              and disposing it disposes the map module.
/// </summary>
public sealed class HomeTests : UiTestContext
{
	#region Test Infrastructure
	/// <summary>
	/// Sets up the calls of the map module the page makes.
	/// </summary>
	/// <returns>The module, for verification.</returns>
	private BunitJSModuleInterop SetUpMap()
	{
		var module = JSInterop.SetupModule(MapScript.ModulePath);
		module.SetupVoid("init", _ => true).SetVoidResult();
		module.SetupVoid("observeSheet", _ => true).SetVoidResult();
		module.SetupVoid("setPin", _ => true).SetVoidResult();
		module.SetupVoid("setCandidate", _ => true).SetVoidResult();
		module.SetupVoid("invalidateSize").SetVoidResult();
		module.SetupVoid("dispose").SetVoidResult();
		return module;
	}
	#endregion

	#region Page Tests
	/// <summary>
	/// Verifies the layout of the page: map, search bar, card button, controls, call to action, sheet and its parts.
	/// </summary>
	[Fact]
	public void Render_Page_ShouldLayOutTheControlsOfTheFirstVersion()
	{
		SetUpMap();

		var cut = Render<Home>();

		cut.Find("#map").Should().NotBeNull();
		cut.Find("#searchInput").GetAttribute("placeholder").Should().Be("Search a street or address");
		cut.Find("#cardBtn").Should().NotBeNull();
		cut.Find("#layersBtn").Should().NotBeNull();
		cut.Find("#carBtn").Should().NotBeNull();
		cut.Find("#trackBtn").Should().NotBeNull();
		cut.Find("#findBtn").TextContent.Should().Be("Find free parking");
		cut.FindAll("#sheet #cardLine, #sheet #status, #sheet #liveLine, #sheet #nearest, #sheet #permBanner").Should().HaveCount(5);
		cut.Find("#carChip").ClassList.Should().Contain("hidden");
	}

	/// <summary>
	/// Verifies that when the map is ready the page follows the user and opens the welcome dialog for a first launch.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_MapReady_ShouldFollowTheUserAndOpenTheWelcomeDialog()
	{
		SetUpMap();

		var host = Render<RadzenDialog>();
		var cut = Render<Home>();

		await cut.WaitForAssertionAsync(() => cut.Find("#trackBtn").ClassList.Should().Contain("active"));
		await Tracker.Received(1).StartAsync(Arg.Any<CancellationToken>());
		await host.WaitForAssertionAsync(() => host.Find("#welcomeStart").Should().NotBeNull());
	}

	/// <summary>
	/// Verifies that "Add my parking card" in the welcome dialog replaces it with the card dialog, in this order, through the one dialog service.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Welcome_AddMyParkingCard_ShouldReplaceTheWelcomeWithTheCardDialog()
	{
		SetUpMap();
		var host = Render<RadzenDialog>();
		Render<Home>();
		await host.WaitForAssertionAsync(() => host.Find("#welcomeStart").Should().NotBeNull());

		await host.Find("#welcomeStart").ClickAsync();

		await host.WaitForAssertionAsync(() => host.FindAll("#cardForm").Should().HaveCount(1));
		host.FindAll("#welcomeDialog").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a tap on the map drops the pin on the map and shows the answer in the status block.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task MapClick_Tap_ShouldDropThePinAndShowTheAnswer()
	{
		var module = SetUpMap();
		AnswerChecks(SpotDecision.Create(Verdict.CantVerify, "Could not read parking.brussels."));
		var cut = Render<Home>();
		var map = cut.FindComponent<MapView>();

		await cut.InvokeAsync(() => map.Instance.OnMapClickAsync(50.83, 4.36));

		await cut.WaitForAssertionAsync(() => cut.Find(".hero-title").TextContent.Should().Be("Can't verify right now"));
		cut.Find(".hero-sub").TextContent.Should().Be("Could not read parking.brussels.");
		module.Invocations["setPin"].Should().NotBeEmpty();
		cut.Find("#trackBtn").ClassList.Should().NotContain("active");
	}

	/// <summary>
	/// Verifies that while the picker is open the root has the picking class, the top bar is the picker header with the confirm bar
	/// below, a tap on the map moves the pin of the picker instead of checking a spot, and the map is told of the new layout.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Picker_Open_ShouldSwitchToPickerModeAndRouteMapTapsToThePin()
	{
		var module = SetUpMap();
		var cut = Render<Home>();
		var map = cut.FindComponent<MapView>();
		var picker = Resolve<PlacePickerViewModel>();

		await cut.InvokeAsync(() => picker.Start(PlaceKind.Home));

		await cut.WaitForAssertionAsync(() => cut.Find("#homeRoot").ClassList.Should().Contain("picking"));
		cut.Find("#pickerTitle").TextContent.Should().Be("Set home");
		cut.FindAll("#searchInput").Should().BeEmpty();
		await cut.InvokeAsync(() => map.Instance.OnMapClickAsync(50.83, 4.36));
		picker.Candidate.Should().Be(new GeoPoint(50.83, 4.36));
		cut.FindAll("#placeSave").Should().HaveCount(1);
		Resolve<SpotCheckViewModel>().Pin.Should().BeNull();
		await cut.WaitForAssertionAsync(() => module.Invocations["setCandidate"].Should().NotBeEmpty());
		await cut.WaitForAssertionAsync(() => module.Invocations["invalidateSize"].Should().NotBeEmpty());

		await cut.InvokeAsync(() => map.Instance.OnCandidateMovedAsync(50.84, 4.37));
		picker.Candidate.Should().Be(new GeoPoint(50.84, 4.37));
		await cut.Find("#placeCancel").ClickAsync();

		await cut.WaitForAssertionAsync(() => cut.Find("#homeRoot").ClassList.Should().NotContain("picking"));
		cut.Find("#searchInput").Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that the layers button opens the popover and collapses the sheet, and that the handle toggles the sheet.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Click_LayersAndHandle_ShouldOpenThePopoverAndToggleTheSheet()
	{
		SetUpMap();
		var cut = Render<Home>();

		await cut.Find("#layersBtn").ClickAsync();

		cut.Find("#layersPanel").ClassList.Should().NotContain("hidden");
		cut.Find("#sheet").ClassList.Should().Contain("compact");
		await cut.Find("#sheetHandle").ClickAsync();
		cut.Find("#sheet").ClassList.Should().NotContain("compact");
	}

	/// <summary>
	/// Verifies that disposing the page disposes the map module.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Dispose_Page_ShouldDisposeTheMapModule()
	{
		var module = SetUpMap();
		Render<Home>();

		await DisposeComponentsAsync();

		module.VerifyInvoke("dispose");
	}
	#endregion
}
