using ParkingTracker.Domain.Cards;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.ReferenceData;
using ParkingTracker.Domain.Tests.TestDoubles;
using ParkingTracker.Domain.Zones;

namespace ParkingTracker.Domain.Tests.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="AreaDecider"/>: which sources an area needs, when a free-spot search makes sense, how
///              surfaces are picked and how the nearest covered spots are chosen.
/// </summary>
public sealed class AreaDeciderTests
{
	#region Test Infrastructure
	private static SurfaceStatus Covered(string street, double northMetres, string id)
	{
		var center = new GeoPoint(ZoneBuilder.Here.Latitude + (northMetres / 111320), ZoneBuilder.Here.Longitude);
		return new SurfaceStatus(ZoneBuilder.Surface(street, 4, center: center, halfSideMetres: 5, id: id), null, "green", SurfaceKind.Covered, null, []);
	}
	#endregion

	#region Source Need Tests
	/// <summary>
	/// Verifies that only area-based cards, or cards with border zones, need the sector areas.
	/// </summary>
	/// <param name="kind">The municipality kind.</param>
	/// <param name="withBorderZone">Whether the card has a border zone.</param>
	/// <param name="expected">Whether the areas are needed.</param>
	[Theory]
	[InlineData(MunicipalityKind.Mailles, false, true)]
	[InlineData(MunicipalityKind.Whole, true, true)]
	[InlineData(MunicipalityKind.Whole, false, false)]
	public void NeedsMailles_ByKindAndBorderZones_ShouldMatchTheCard(MunicipalityKind kind, bool withBorderZone, bool expected)
	{
		var context = ZoneBuilder.Context(kind, ZoneBuilder.Card(lims: withBorderZone ? ["LIM1"] : null));

		AreaDecider.NeedsMailles(context).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that house numbers are not needed without a street-list sector.
	/// </summary>
	[Fact]
	public void NeedsAddresses_StreetsKindWithoutSector_ShouldBeFalse()
	{
		AreaDecider.NeedsAddresses(ZoneBuilder.Context(MunicipalityKind.Streets)).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a free-spot search needs a valid card; a flagged official list only counts for municipalities with a sector.
	/// </summary>
	[Fact]
	public void CanSearchSpots_ByCardAndList_ShouldRequireAValidCard()
	{
		var valid = ZoneBuilder.Context(MunicipalityKind.Whole);
		var flagged = valid with { OfficialList = OfficialListStatus.CouldNotCheck("down", DateTimeOffset.UnixEpoch) };
		var expired = valid with { Card = new ParkingCard("test", null, new CardPeriod(null, new DateOnly(2026, 1, 1)), [], [], []) };
		var today = new DateOnly(2026, 11, 17);

		AreaDecider.CanSearchSpots(valid, today).Should().BeTrue();
		AreaDecider.CanSearchSpots(expired, today).Should().BeFalse();
		AreaDecider.CanSearchSpots(valid with { Card = null }, today).Should().BeFalse();
		AreaDecider.CanSearchSpots(flagged, today).Should().BeTrue("a street-list problem only counts when the municipality has a sector");
	}
	#endregion

	#region Free Spot Tests
	/// <summary>
	/// Verifies that spots are ordered by distance, one per street, none within 3 m, and limited to the count.
	/// </summary>
	[Fact]
	public void FindFreeSpots_Surfaces_ShouldPickTheNearestPerStreetAwayFromTheOrigin()
	{
		SurfaceStatus[] surfaces =
		[
			Covered("Rue Ici", 0, "1"),
			Covered("Rue Proche", 30, "2"),
			Covered("Rue Proche", 40, "3"),
			Covered("Rue Loin", 90, "4"),
			Covered("Rue Plus Loin", 150, "5"),
		];

		var spots = AreaDecider.FindFreeSpots(ZoneBuilder.Here, surfaces, 2);

		spots.Select(spot => spot.Street).Should().Equal("Rue Proche", "Rue Loin");
		spots[0].Metres.Should().BeInRange(20, 30);
	}

	/// <summary>
	/// Verifies that surfaces that are not covered give no spot.
	/// </summary>
	[Fact]
	public void FindFreeSpots_NoCoveredSurface_ShouldFindNothing()
	{
		var pay = Covered("Rue Payante", 30, "1") with { Kind = SurfaceKind.Pay };

		AreaDecider.FindFreeSpots(ZoneBuilder.Here, [pay]).Should().BeEmpty();
	}
	#endregion
}
