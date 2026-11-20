using Bunit;
using Radzen.Blazor;
using ParkingTracker.Shared.Features.MapLayers.Services;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.Shared.Features.Shared.Layout;
using ParkingTracker.Shared.Features.SpotCheck.Pages;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Shared.Tests.Features.Shared.Layout;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the <see cref="Routes"/> router: the root address shows the home page inside the layout.
/// </summary>
public sealed class RoutesTests : UiTestContext
{
	#region Routing Tests
	/// <summary>
	/// Verifies that the router of the app shows the home page at the root address, inside the layout that holds the toast and the Radzen hosts.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Routes_RootAddress_ShouldShowTheHomePageInTheLayout()
	{
		SetUpMap();

		var cut = Render<Routes>();

		await cut.WaitForAssertionAsync(() => cut.FindComponent<Home>().Should().NotBeNull());
		cut.FindComponent<Toast>().Should().NotBeNull();
		cut.FindComponent<RadzenComponents>().Should().NotBeNull();
	}
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Sets up the calls of the map module the home page makes.
	/// </summary>
	private void SetUpMap()
	{
		var module = JSInterop.SetupModule(MapScript.ModulePath);
		module.SetupVoid("init", _ => true).SetVoidResult();
		module.SetupVoid("observeSheet", _ => true).SetVoidResult();
		module.SetupVoid("setPin", _ => true).SetVoidResult();
		module.SetupVoid("setCandidate", _ => true).SetVoidResult();
		module.SetupVoid("invalidateSize").SetVoidResult();
		module.SetupVoid("dispose").SetVoidResult();
	}
	#endregion
}
