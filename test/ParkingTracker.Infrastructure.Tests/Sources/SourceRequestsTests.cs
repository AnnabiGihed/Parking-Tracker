using ParkingTracker.Domain.Geometry;
using ParkingTracker.Infrastructure.Sources;

namespace ParkingTracker.Infrastructure.Tests.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SourceRequests"/>: the request addresses are the ones the first version of the app sent.
///              The parity tests prove the same against the recorded requests.
/// </summary>
public sealed class SourceRequestsTests
{
	#region Test Infrastructure
	private static readonly GeoBox Box = new(50.8243, 4.3591, 50.8257, 4.3621);
	#endregion

	#region Address Tests
	/// <summary>
	/// Verifies the zones request: layer, projection, count and a six-decimal bounding box in EPSG:4326.
	/// </summary>
	[Fact]
	public void Zones_Box_ShouldBuildTheWfsRequest()
	{
		SourceRequests.Zones(Box).Should().Be(
			"https://ows.parking.brussels/ws/ParkingBrussels/ows?service=WFS&version=2.0.0&request=GetFeature&outputFormat=application/json"
			+ "&typeNames=ParkingBrussels:regulation_zones&srsName=EPSG:3857&count=3000&bbox=50.824300,4.359100,50.825700,4.362100,urn:ogc:def:crs:EPSG::4326");
	}

	/// <summary>
	/// Verifies the address points request, which asks for the house number and the position only.
	/// </summary>
	[Fact]
	public void AddressPoints_Box_ShouldAskForTheNumberAndThePositionOnly()
	{
		SourceRequests.AddressPoints(Box).Should().Contain("typeNames=bm_urbis:urbadm_adpt").And.Contain("propertyName=adrn,geom").And.Contain("count=5000");
	}

	/// <summary>
	/// Verifies the layer requests of the other WFS sources.
	/// </summary>
	[Fact]
	public void Layers_Box_ShouldNameTheirLayers()
	{
		SourceRequests.Spaces(Box).Should().StartWith("https://ows.parking.brussels/").And.Contain("typeNames=ParkingBrussels:on_street");
		SourceRequests.Mailles(Box).Should().Contain("typeNames=ParkingBrussels:permit_secteurs_mailles");
		SourceRequests.Roadworks(Box).Should().StartWith("https://data.mobility.brussels/geoserver/ows?").And.Contain("typeNames=bm_osiris:chantiers_actu");
		SourceRequests.TrafficEvents(Box).Should().Contain("typeNames=bm_traffic:events");
	}

	/// <summary>
	/// Verifies that the rules request asks for every zone, or for one zone code, encoded.
	/// </summary>
	[Fact]
	public void Rules_WithAndWithoutACode_ShouldBuildTheRequest()
	{
		SourceRequests.Rules(null).Should().Be("https://api.mobility.brussels/datasets/v1/parking/collections/regulations_rules/items/?f=json&limit=500");
		SourceRequests.Rules("BRU 4&").Should().EndWith("?f=json&limit=500&code=BRU%204%26");
	}

	/// <summary>
	/// Verifies that the numbers of the box do not depend on the culture of the machine.
	/// </summary>
	[Fact]
	public void Zones_FrenchCulture_ShouldStillUseDecimalPoints()
	{
		var previous = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-BE");

			SourceRequests.Zones(Box).Should().Contain("bbox=50.824300,4.359100,");
		}
		finally
		{
			System.Globalization.CultureInfo.CurrentCulture = previous;
		}
	}
	#endregion
}
