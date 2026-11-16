using ParkingTracker.Infrastructure.Parsing;
using ParkingTracker.Infrastructure.Sources;

namespace ParkingTracker.Infrastructure.Tests.Parsing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ZoneSurfaceParser"/>: fields, shapes, the filters (no geometry, disabled, tunnels and
///              the like) and the failures for an unexpected or failed answer.
/// </summary>
public sealed class ZoneSurfaceParserTests
{
	#region Test Infrastructure
	private const string Square = "{\"type\":\"Polygon\",\"coordinates\":[[[485000,6590000],[485100,6590000],[485100,6590100],[485000,6590100],[485000,6590000]]]}";

	private static string Feature(string properties, string geometry = Square) => $"{{\"type\":\"Feature\",\"geometry\":{geometry},\"properties\":{{{properties}}}}}";

	private static string Collection(params string[] features) => $"{{\"type\":\"FeatureCollection\",\"features\":[{string.Join(",", features)}]}}";
	#endregion

	#region Parse Tests
	/// <summary>
	/// Verifies that the fields and the shape of a surface are read.
	/// </summary>
	[Fact]
	public void Parse_Surface_ShouldReadFieldsAndShape()
	{
		var json = Collection(Feature("\"id\":3114508,\"pn_name_fr\":\"Rue Lens\",\"pn_name_du\":\"Lensstraat\",\"mu_name_fr\":\"IXELLES\",\"typreg\":3,\"ZoneId1\":\"IXE6\""));

		var result = ZoneSurfaceParser.Parse(json);

		var surface = result.Value.Should().ContainSingle().Subject;
		surface.Id.Should().Be("3114508");
		surface.FrenchName.Should().Be("Rue Lens");
		surface.DutchName.Should().Be("Lensstraat");
		surface.Municipality.Should().Be("IXELLES");
		surface.ColourCode.Should().Be(3);
		surface.ZoneCode.Should().Be("IXE6");
		surface.Bounds.Should().Be(new ParkingTracker.Domain.Geometry.MercatorBounds(485000, 6590000, 485100, 6590100));
	}

	/// <summary>
	/// Verifies that a multi-polygon is read with all its polygons.
	/// </summary>
	[Fact]
	public void Parse_MultiPolygon_ShouldReadEveryPolygon()
	{
		const string multi = "{\"type\":\"MultiPolygon\",\"coordinates\":[[[[0,0],[1,0],[1,1],[0,0]]],[[[5,5],[6,5],[6,6],[5,5]]]]}";

		var result = ZoneSurfaceParser.Parse(Collection(Feature("\"pn_name_fr\":\"Rue A\"", multi)));

		result.Value.Single().Shape.Polygons.Should().HaveCount(2);
	}

	/// <summary>
	/// Verifies that the lower-case zone identifier is used when the upper-case one is missing.
	/// </summary>
	[Fact]
	public void Parse_LowerCaseZoneId_ShouldBeUsedAsAFallback()
	{
		var result = ZoneSurfaceParser.Parse(Collection(Feature("\"pn_name_fr\":\"Rue A\",\"zoneid1\":\"X1\"")));

		result.Value.Single().ZoneCode.Should().Be("X1");
	}

	/// <summary>
	/// Verifies that features without a geometry, disabled surfaces and tunnels, viaducts, trémies and bridges are left out.
	/// </summary>
	[Fact]
	public void Parse_NotParkableFeatures_ShouldBeLeftOut()
	{
		var json = Collection(
			Feature("\"pn_name_fr\":\"Rue Ouverte\""),
			Feature("\"pn_name_fr\":\"Rue Fermée\",\"disabled_on\":\"2025-01-01T00:00:00Z\""),
			Feature("\"pn_name_fr\":\"Tunnel Bailli\""),
			Feature("\"pn_name_fr\":\"Viaduc de Cureghem\""),
			Feature("\"pn_name_fr\":\"Trémie Stéphanie\""),
			Feature("\"pn_name_fr\":\"Pont Van Praet\""),
			Feature("\"pn_name_fr\":\"Pontoise\""),
			"{\"type\":\"Feature\",\"geometry\":null,\"properties\":{\"pn_name_fr\":\"Rue Sans Forme\"}}");

		var result = ZoneSurfaceParser.Parse(json);

		result.Value.Select(surface => surface.FrenchName).Should().Equal("Rue Ouverte", "Pontoise");
	}

	/// <summary>
	/// Verifies that an answer that is not a feature collection, or not JSON, is a bad-response failure with the message of the first version.
	/// </summary>
	/// <param name="json">The answer.</param>
	[Theory]
	[InlineData("{}")]
	[InlineData("{\"features\":null}")]
	[InlineData("not json")]
	[InlineData("")]
	public void Parse_UnexpectedAnswer_ShouldFailWithABadResponse(string json)
	{
		var result = ZoneSurfaceParser.Parse(json);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("ParkingBrussels.BadResponse");
		result.Error.Message.Should().Be("parking.brussels: unexpected answer");
	}

	/// <summary>
	/// Verifies that an HTTP error status is reported with the status and no parsing is attempted.
	/// </summary>
	[Fact]
	public void Read_HttpError_ShouldFailWithTheStatus()
	{
		var result = new SourceResponse(500, "Internal Server Error").Read(SourceKind.ParkingBrussels, ZoneSurfaceParser.What, ZoneSurfaceParser.Parse);

		result.Error.Code.Should().Be("ParkingBrussels.Unavailable");
		result.Error.Message.Should().Be("parking.brussels HTTP 500");
	}
	#endregion
}
