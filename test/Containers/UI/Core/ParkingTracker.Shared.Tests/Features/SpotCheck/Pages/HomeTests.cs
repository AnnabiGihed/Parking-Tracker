using Bunit;
using NSubstitute;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Shared.Features.MapLayers.Components;
using ParkingTracker.Shared.Features.MapLayers.Services;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.Shared.Features.SpotCheck.Pages;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;

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

		var cut = Render<Home>();

		await cut.WaitForAssertionAsync(() => cut.Find("#trackBtn").ClassList.Should().Contain("active"));
		await Tracker.Received(1).StartAsync(Arg.Any<CancellationToken>());
		await cut.WaitForAssertionAsync(() => cut.Find("#welcomeStart").Should().NotBeNull());
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
