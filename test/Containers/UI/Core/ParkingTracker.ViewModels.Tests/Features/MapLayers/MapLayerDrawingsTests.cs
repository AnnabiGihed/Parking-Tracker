using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="MapLayerDrawings"/>: the style key of a street outside paid hours, the rounded
///              coordinates, the compact shapes, and the escaping of every text that reaches a popup.
/// </summary>
public sealed class MapLayerDrawingsTests
{
	#region Static Instances
	/// <summary>
	/// A surface with an empty shape.
	/// </summary>
	private static readonly ZoneSurface Surface = new("1", "Avenue Louise", "Louizalaan", "IXELLES", 1, null, new SurfaceShape([]));

	/// <summary>
	/// A rule that is never paid, so that every place is outside paid hours.
	/// </summary>
	private static readonly ParkingRule NeverPaid = new(null, null, new HashSet<DayOfWeek>(), null, null, false, null, null, null, null);
	#endregion

	#region Street Tests
	/// <summary>
	/// Verifies that a paid street outside paid hours is free for everyone right now, a part-covered one is covered, and that without
	/// a rule the kind decides.
	/// </summary>
	[Fact]
	public void Streets_OutsidePaidHours_ShouldMapToFreeNowAndCovered()
	{
		static SurfaceStatus Status(SurfaceKind kind, ParkingRule? rule) => new(Surface, rule, "#000", kind, null, []);

		var styles = MapLayerDrawings.Streets(
			[Status(SurfaceKind.Pay, NeverPaid), Status(SurfaceKind.Mixed, NeverPaid), Status(SurfaceKind.Pay, null), Status(SurfaceKind.Covered, NeverPaid), Status(SurfaceKind.Red, NeverPaid)],
			UiFixture.Start).Select(item => item.Style);

		styles.Should().Equal("freenow", "covered", "pay", "covered", "freenow");
	}

	/// <summary>
	/// Verifies that a shape is converted from Web Mercator to latitude and longitude rounded to six decimals.
	/// </summary>
	[Fact]
	public void Shape_MercatorPolygon_ShouldGiveRoundedLatitudeLongitudePairs()
	{
		var ring = new[] { UiFixture.Here, new GeoPoint(50.8252, 4.3610), new GeoPoint(50.8254, 4.3600), UiFixture.Here }.Select(WebMercator.ToMercator).ToList();

		var shape = MapLayerDrawings.Shape(new SurfaceShape([[ring]]));

		var pairs = shape.Should().ContainSingle().Which.Should().ContainSingle().Which;
		pairs.Should().HaveCount(4);
		pairs[0][0].Should().BeApproximately(50.825, 0.000002);
		pairs[0][1].Should().BeApproximately(4.3606, 0.000002);
		Math.Round(pairs[0][0], 6).Should().Be(pairs[0][0]);
	}
	#endregion

	#region Popup Tests
	/// <summary>
	/// Verifies that the text of a roadworks site is escaped in its popup and that its link becomes an action value, not a raw address in markup.
	/// </summary>
	[Fact]
	public void WorkAreas_HostileText_ShouldBeEscapedInThePopup()
	{
		var work = new WorkItem(WorkKind.Works, null, new SurfaceShape([]), "<script>alert(1)</script>", "Rue \"X\" & Y", "2026-12-31", "2026-11-01", "<b>by</b>", "https://example.org/?a=1&b=\"2\"");

		var popup = MapLayerDrawings.WorkAreas([work]).Should().ContainSingle().Subject.Popup!;

		popup.Should().NotContain("<script>").And.Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
		popup.Should().Contain("Rue &quot;X&quot; &amp; Y").And.Contain("&lt;b&gt;by&lt;/b&gt;");
		popup.Should().Contain("From 01/11/2026 until 31/12/2026");
		popup.Should().Contain("data-act=\"link\" data-id=\"https://example.org/?a=1&amp;b=&quot;2&quot;\"");
	}

	/// <summary>
	/// Verifies that a car park popup escapes the service texts and offers Waze and Google Maps with the position.
	/// </summary>
	[Fact]
	public void CarParks_Popup_ShouldEscapeTextAndOfferNavigation()
	{
		var park = new CarPark(new GeoPoint(50.8250004, 4.3606), "Park <1>", "Av. Louise 216", "350", "4", "Q-Park", "+3225000000", "1.9");

		var marker = MapLayerDrawings.CarParks([park]).Should().ContainSingle().Subject;

		marker.Lat.Should().Be(50.825);
		marker.Label.Should().Be("🅿️");
		marker.Popup.Should().Contain("<b>Park &lt;1&gt;</b>").And.Contain("350 places, 4 ♿").And.Contain("Max height 1.9 m");
		marker.Popup.Should().Contain("data-act=\"waze\" data-id=\"50.825000,4.360600\"").And.Contain("data-act=\"gmaps\" data-id=\"50.825000,4.360600\"");
		marker.Popup.Should().Contain("data-id=\"tel:+3225000000\"");
	}

	/// <summary>
	/// Verifies the charging point popup with unknown values shown as question marks, as the first version did.
	/// </summary>
	[Fact]
	public void Chargers_UnknownValues_ShouldShowQuestionMarks()
	{
		var marker = MapLayerDrawings.Chargers([new ChargingPoint(UiFixture.Here, null, null, null, null, null)]).Single();

		marker.Popup.Should().Contain("<b>⚡ Charging point</b>").And.Contain("? point(s) · ? V");
	}

	/// <summary>
	/// Verifies that the label of a marker is escaped in the payload, so that markup from a service cannot run in the map page.
	/// </summary>
	[Fact]
	public void Marker_HostileLabel_ShouldBeEscapedInThePayload()
	{
		var marker = MapLayerDrawings.Marker(UiFixture.Here, "<img src=x onerror=alert(1)>", null);

		marker.Label.Should().Be("&lt;img src=x onerror=alert(1)&gt;");
		marker.Label.Should().NotContain("<").And.NotContain(">");
	}

	/// <summary>
	/// Verifies that the emoji of the markers pass the escaping unchanged.
	/// </summary>
	[Fact]
	public void Marker_Emoji_ShouldStayUnchanged()
	{
		MapLayerDrawings.Marker(UiFixture.Here, "🅿️", null).Label.Should().Be("🅿️");
	}

	/// <summary>
	/// Verifies that a reserved space gets a marker with its emoji and an ordinary space a dot without popup.
	/// </summary>
	[Fact]
	public void Spaces_ReservedAndOrdinary_ShouldGiveMarkerAndDot()
	{
		var markers = MapLayerDrawings.Spaces([new ReservedSpace(UiFixture.Here, 5), new ReservedSpace(UiFixture.Here, 0)]);

		markers[0].Style.Should().Be(MapLayerDrawings.MarkerStyle);
		markers[0].Label.Should().Be("♿");
		markers[0].Popup.Should().Contain("<b>♿ Disabled (PMR) space</b>").And.Contain("You can't park here with your residents' card.");
		markers[1].Style.Should().Be(MapLayerDrawings.SpaceStyle);
		markers[1].Popup.Should().BeNull();
	}
	#endregion
}
