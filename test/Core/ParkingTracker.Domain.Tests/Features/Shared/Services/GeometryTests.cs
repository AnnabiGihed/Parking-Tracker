using System.Globalization;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.Shared.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the geometry of live.js: Web Mercator conversions, distance, point-in-ring, the box padding
///              and the closest point of a street surface.
/// </summary>
public sealed class GeometryTests
{
	#region Constants
	/// <summary>
	/// The latitude of the place the tests start from.
	/// </summary>
	private const double Latitude = 50.85;

	/// <summary>
	/// The longitude of the place the tests start from.
	/// </summary>
	private const double Longitude = 4.35;
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Creates the closed ring of a square around a center.
	/// </summary>
	/// <param name="center">Center of the square.</param>
	/// <param name="half">Half the side length.</param>
	/// <returns>Ring of points.</returns>
	private static IReadOnlyList<MercatorPoint> Square(MercatorPoint center, double half)
	{
		return
		[
			new MercatorPoint(center.X - half, center.Y - half),
			new MercatorPoint(center.X + half, center.Y - half),
			new MercatorPoint(center.X + half, center.Y + half),
			new MercatorPoint(center.X - half, center.Y + half),
			new MercatorPoint(center.X - half, center.Y - half),
		];
	}

	/// <summary>
	/// Converts the standard position to a Mercator point.
	/// </summary>
	/// <returns>Mercator point.</returns>
	private static MercatorPoint Center() => WebMercator.ToMercator(new GeoPoint(Latitude, Longitude));

	/// <summary>
	/// Creates a surface shape from polygons.
	/// </summary>
	/// <param name="polygons">Polygons of the surface.</param>
	/// <returns>Surface shape.</returns>
	private static SurfaceShape Surface(IReadOnlyList<IReadOnlyList<IReadOnlyList<MercatorPoint>>> polygons) => new(polygons);
	#endregion

	#region WebMercator Tests
	/// <summary>
	/// Verifies the projection of known points.
	/// </summary>
	[Fact]
	public void ToMercator_KnownPoints_ShouldMatchEpsg3857()
	{
		var origin = WebMercator.ToMercator(new GeoPoint(0, 0));
		var tenDegrees = WebMercator.ToMercator(new GeoPoint(0, 10));
		var date = WebMercator.ToMercator(new GeoPoint(50, 0));

		origin.X.Should().BeApproximately(0, 1e-6);
		origin.Y.Should().BeApproximately(0, 1e-6);
		tenDegrees.X.Should().BeApproximately(1113194.9079, 0.001);
		date.Y.Should().BeApproximately(6446275.84, 0.01);
	}

	/// <summary>
	/// Verifies that converting to Mercator and back returns the same position.
	/// </summary>
	[Fact]
	public void ToGeo_AfterToMercator_ShouldReturnTheSamePosition()
	{
		var back = WebMercator.ToGeo(WebMercator.ToMercator(new GeoPoint(Latitude, Longitude)));

		back.Latitude.Should().BeApproximately(Latitude, 1e-9);
		back.Longitude.Should().BeApproximately(Longitude, 1e-9);
	}
	#endregion

	#region Distance Tests
	/// <summary>
	/// Verifies the distance along a meridian and for identical points.
	/// </summary>
	[Fact]
	public void Distance_OneDegreeOfLatitude_ShouldBe111Kilometres()
	{
		GeoMath.Distance(new GeoPoint(0, 0), new GeoPoint(1, 0)).Should().BeApproximately(111194.93, 0.01);
		GeoMath.Distance(new GeoPoint(Latitude, Longitude), new GeoPoint(Latitude, Longitude)).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a degree of longitude is shorter by the cosine of the latitude.
	/// </summary>
	[Fact]
	public void Distance_OneDegreeOfLongitude_ShouldShrinkWithLatitude()
	{
		var metres = GeoMath.Distance(new GeoPoint(60, 0), new GeoPoint(60, 1));

		metres.Should().BeApproximately(111194.93 * 0.5, 0.01);
	}
	#endregion

	#region InRing Tests
	/// <summary>
	/// Verifies the even-odd rule for points inside and outside a square, and for an empty ring.
	/// </summary>
	[Fact]
	public void InRing_PointsAroundASquare_ShouldBeInsideOnlyWithinIt()
	{
		var ring = Square(new MercatorPoint(5, 5), 5);

		GeoMath.InRing(new MercatorPoint(5, 5), ring).Should().BeTrue();
		GeoMath.InRing(new MercatorPoint(15, 5), ring).Should().BeFalse();
		GeoMath.InRing(new MercatorPoint(5, -1), ring).Should().BeFalse();
		GeoMath.InRing(new MercatorPoint(5, 5), []).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a missing ring is refused.
	/// </summary>
	[Fact]
	public void InRing_Null_ShouldThrow()
	{
		var act = () => GeoMath.InRing(new MercatorPoint(0, 0), null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Pad Tests
	/// <summary>
	/// Verifies the default padding of about 45 m, 1.6 times as much in longitude.
	/// </summary>
	[Fact]
	public void Pad_DefaultPadding_ShouldGrowLongitudeBy1Point6()
	{
		var padded = new GeoBox(50, 4, 51, 5).Pad();

		padded.South.Should().BeApproximately(49.9996, 1e-12);
		padded.West.Should().BeApproximately(3.99936, 1e-12);
		padded.North.Should().BeApproximately(51.0004, 1e-12);
		padded.East.Should().BeApproximately(5.00064, 1e-12);
	}

	/// <summary>
	/// Verifies a custom padding.
	/// </summary>
	[Fact]
	public void Pad_CustomPadding_ShouldGrowByThatAmount()
	{
		var padded = new GeoBox(0, 0, 1, 1).Pad(0.01);

		padded.Should().Be(new GeoBox(-0.01, -0.016, 1.01, 1.016));
	}
	#endregion

	#region SurfaceFilter Tests
	/// <summary>
	/// Verifies that surfaces named tunnel, viaduc, trémie or pont are not parkable, whatever the case or the spaces around.
	/// </summary>
	/// <param name="name">The French street name.</param>
	[Theory]
	[InlineData("Tunnel Bailli")]
	[InlineData("Viaduc de Cureghem")]
	[InlineData("Trémie Louise")]
	[InlineData("TREMIE Louise")]
	[InlineData("Pont de Luttre")]
	[InlineData("  Tunnel Stéphanie")]
	[InlineData("Pont")]
	public void IsParkable_TunnelViaductOrBridge_ShouldBeFalse(string name)
	{
		SurfaceFilter.IsParkable(name).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the answer is the same under the Turkish culture, where a culture-sensitive case-insensitive match
	/// would not recognise <c>VIADUC</c> (dotted and dotless i).
	/// </summary>
	/// <param name="name">The French street name.</param>
	[Theory]
	[InlineData("VIADUC de Cureghem")]
	[InlineData("viaduc de Cureghem")]
	[InlineData("TRÉMIE Louise")]
	public void IsParkable_ViaductUnderTurkishCulture_ShouldBeFalse(string name)
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
		try
		{
			SurfaceFilter.IsParkable(name).Should().BeFalse();
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	/// <summary>
	/// Verifies that an accented letter right after the word still ends it, like the word boundary of the web app.
	/// </summary>
	[Fact]
	public void IsParkable_AccentedLetterAfterTheWord_ShouldEndTheWord()
	{
		SurfaceFilter.IsParkable("Ponté").Should().BeFalse();
	}

	/// <summary>
	/// Verifies that ordinary streets, names that only contain or start the word, and missing names are parkable.
	/// </summary>
	/// <param name="name">The French street name.</param>
	[Theory]
	[InlineData("Avenue Louise")]
	[InlineData("Rue du Pont")]
	[InlineData("Pontsstraat")]
	[InlineData("")]
	[InlineData(null)]
	public void IsParkable_OrdinaryStreet_ShouldBeTrue(string? name)
	{
		SurfaceFilter.IsParkable(name).Should().BeTrue();
	}
	#endregion

	#region SurfaceShape Tests
	/// <summary>
	/// Verifies that a position inside a surface is at distance 0 and is its own closest point.
	/// </summary>
	[Fact]
	public void ClosestTo_PositionInside_ShouldBeAtDistanceZero()
	{
		var surface = Surface([[Square(Center(), 50)]]);
		var position = new GeoPoint(Latitude, Longitude);

		var closest = surface.ClosestTo(position);

		closest.Metres.Should().Be(0);
		closest.Point.Should().Be(position);
	}

	/// <summary>
	/// Verifies the distance and the closest point of a position outside a surface: the metres are scaled by the cosine of the latitude.
	/// </summary>
	[Fact]
	public void ClosestTo_PositionOutside_ShouldReturnDistanceAndEdgePoint()
	{
		var center = Center();
		var surface = Surface([[Square(center, 50)]]);
		var position = WebMercator.ToGeo(new MercatorPoint(center.X, center.Y + 150));
		var expectedEdge = WebMercator.ToGeo(new MercatorPoint(center.X, center.Y + 50));

		var closest = surface.ClosestTo(position);

		closest.Metres.Should().BeApproximately(100 * Math.Cos(position.Latitude * Math.PI / 180), 1e-6);
		closest.Point.Latitude.Should().BeApproximately(expectedEdge.Latitude, 1e-9);
		closest.Point.Longitude.Should().BeApproximately(expectedEdge.Longitude, 1e-9);
	}

	/// <summary>
	/// Verifies that a position in a hole is outside the surface and measured to the edge of the hole.
	/// </summary>
	[Fact]
	public void ClosestTo_PositionInHole_ShouldMeasureToTheHoleEdge()
	{
		var center = Center();
		var surface = Surface([[Square(center, 50), Square(center, 10)]]);

		var closest = surface.ClosestTo(new GeoPoint(Latitude, Longitude));

		closest.Metres.Should().BeApproximately(10 * Math.Cos(Latitude * Math.PI / 180), 1e-6);
	}

	/// <summary>
	/// Verifies that the nearest of several polygons is used.
	/// </summary>
	[Fact]
	public void ClosestTo_TwoPolygons_ShouldUseTheNearest()
	{
		var center = Center();
		var near = Square(new MercatorPoint(center.X, center.Y + 200), 10);
		var far = Square(new MercatorPoint(center.X, center.Y + 1000), 10);

		var closest = Surface([[far], [near]]).ClosestTo(new GeoPoint(Latitude, Longitude));

		closest.Metres.Should().BeApproximately(190 * Math.Cos(Latitude * Math.PI / 180), 1e-6);
	}

	/// <summary>
	/// Verifies that a surface without edges is infinitely far and answers the position itself.
	/// </summary>
	[Fact]
	public void ClosestTo_EmptySurface_ShouldBeInfinitelyFar()
	{
		var closest = Surface([]).ClosestTo(new GeoPoint(Latitude, Longitude));

		closest.Metres.Should().Be(double.PositiveInfinity);
		closest.Point.Latitude.Should().BeApproximately(Latitude, 1e-9);
		closest.Point.Longitude.Should().BeApproximately(Longitude, 1e-9);
	}

	/// <summary>
	/// Verifies that a polygon without rings is ignored and a degenerate edge does not break the search.
	/// </summary>
	[Fact]
	public void ClosestTo_PolygonWithoutRingsAndZeroLengthEdge_ShouldStillAnswer()
	{
		var center = Center();
		IReadOnlyList<IReadOnlyList<MercatorPoint>> noRings = [];
		IReadOnlyList<IReadOnlyList<MercatorPoint>> point = [[new MercatorPoint(center.X, center.Y + 30), new MercatorPoint(center.X, center.Y + 30)]];

		var closest = Surface([noRings, point]).ClosestTo(new GeoPoint(Latitude, Longitude));

		closest.Metres.Should().BeApproximately(30 * Math.Cos(Latitude * Math.PI / 180), 1e-6);
	}

	/// <summary>
	/// Verifies that the bounds cover the outer rings of all polygons and are inverted when there are none.
	/// </summary>
	[Fact]
	public void Bounds_Polygons_ShouldCoverTheOuterRings()
	{
		var surface = Surface([[Square(new MercatorPoint(0, 0), 10), Square(new MercatorPoint(0, 0), 2)], [Square(new MercatorPoint(100, 50), 5)]]);

		surface.Bounds.Should().Be(new MercatorBounds(-10, -10, 105, 55));
		Surface([]).Bounds.Should().Be(new MercatorBounds(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity));
	}
	#endregion
}
