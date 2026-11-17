using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;

namespace ParkingTracker.ViewModels.Tests.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="LayerRules"/>: the zoom level from which each layer shows, the zone image when zoomed out,
///              and the house-number detail from zoom 16.
/// </summary>
public sealed class LayerRulesTests
{
	#region Static Instances
	/// <summary>
	/// Every layer switched on.
	/// </summary>
	private static readonly LayerSettings AllOn = new(true, true, true, true, true);
	#endregion

	#region Zoom Tests
	/// <summary>
	/// Verifies the zoom level at which each layer starts to show.
	/// </summary>
	/// <param name="zoom">The zoom level.</param>
	/// <param name="streets">Whether the street layer shows.</param>
	/// <param name="spaces">Whether the parking spaces show.</param>
	/// <param name="works">Whether the roadworks show.</param>
	/// <param name="carParks">Whether the car parks show.</param>
	/// <param name="chargers">Whether the charging points show.</param>
	[Theory]
	[InlineData(13, false, false, false, false, false)]
	[InlineData(14, false, false, false, true, false)]
	[InlineData(15, true, false, true, true, true)]
	[InlineData(16, true, false, true, true, true)]
	[InlineData(17, true, true, true, true, true)]
	public void ActiveAt_Zoom_ShouldShowEachLayerFromItsMinimum(double zoom, bool streets, bool spaces, bool works, bool carParks, bool chargers)
	{
		LayerRules.ActiveAt(AllOn, zoom).Should().Be(new LayerSettings(streets, spaces, works, carParks, chargers));
	}

	/// <summary>
	/// Verifies that a layer that is switched off stays off at any zoom.
	/// </summary>
	[Fact]
	public void ActiveAt_SwitchedOff_ShouldStayOff()
	{
		LayerRules.ActiveAt(new LayerSettings(false, false, false, false, false), 19).Should().Be(new LayerSettings(false, false, false, false, false));
	}

	/// <summary>
	/// Verifies that the zone image shows only zoomed out with the street layer on.
	/// </summary>
	[Fact]
	public void ShowsZoneImage_Cases_ShouldShowOnlyZoomedOutWithStreetsOn()
	{
		LayerRules.ShowsZoneImage(AllOn, 14).Should().BeTrue();
		LayerRules.ShowsZoneImage(AllOn, 15).Should().BeFalse();
		LayerRules.ShowsZoneImage(new LayerSettings(false), 10).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that house numbers are fetched from zoom 16.
	/// </summary>
	[Fact]
	public void NeedsHouseNumbers_Zoom_ShouldStartAtSixteen()
	{
		LayerRules.NeedsHouseNumbers(15).Should().BeFalse();
		LayerRules.NeedsHouseNumbers(16).Should().BeTrue();
	}
	#endregion
}
