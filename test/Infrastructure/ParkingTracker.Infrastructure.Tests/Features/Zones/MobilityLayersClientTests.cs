using System.Net;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Zones;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="MobilityLayersClient"/> over a stub HTTP handler: the car parks and charging points of Brussels
///              Mobility. They prove the exact requests, the reading of the answers and the error mapping.
/// </summary>
public sealed class MobilityLayersClientTests
{
	#region Constants
	/// <summary>
	/// Two car parks, one of them not active, with Web Mercator coordinates in Brussels.
	/// </summary>
	private const string CarParks = """
		{"type":"FeatureCollection","features":[
		 {"type":"Feature","geometry":{"type":"Point","coordinates":[485000,6590000]},"properties":{"name_fr":"Parking Louise","road_fr":"Avenue Louise","housenr":"216","capacity":350,"disabledcapacity":0,"operator_fr":"Q-Park","contact_phone":"+3225000000","maxheight":1.9,"active":true}},
		 {"type":"Feature","geometry":{"type":"Point","coordinates":[485100,6590100]},"properties":{"name_fr":"Closed","active":false}},
		 {"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]},"properties":{"name_fr":"No point"}}
		]}
		""";

	/// <summary>
	/// One charging point with an unknown power.
	/// </summary>
	private const string Chargers = """
		{"type":"FeatureCollection","features":[
		 {"type":"Feature","geometry":{"type":"Point","coordinates":[485000,6590000]},"properties":{"operator_name":"Allego","charging_points":2,"power":0,"connector_standard":"IEC 62196","connector_type":"Type 2"}}
		]}
		""";
	#endregion

	#region Static Instances
	/// <summary>
	/// The bounding box the tests ask about.
	/// </summary>
	private static readonly GeoBox Box = new(50.8243, 4.3591, 50.8257, 4.3621);
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Creates the mobility client on top of a stub handler.
	/// </summary>
	/// <param name="handler">Stub handler that answers the requests.</param>
	/// <returns>The mobility layers client.</returns>
	private static MobilityLayersClient Mobility(StubHttpMessageHandler handler) => new(new HttpClient(handler), SourceTimeouts.Default);
	#endregion

	#region Car Park Tests
	/// <summary>
	/// Verifies the request and that inactive car parks and features without a point are left out while zero values count as unknown.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCarParks_Answer_ShouldSendBuiltAddressAndReadActivePoints()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, CarParks);

		var result = await Mobility(handler).GetCarParksAsync(Box, CancellationToken.None);

		handler.Requests.Should().Equal($"GET {SourceRequests.CarParks(Box)}");
		handler.Requests[0].Should().Contain("typeNames=bm_parking:parkingpublic");
		var park = result.Value.Should().ContainSingle().Subject;
		park.Name.Should().Be("Parking Louise");
		park.Address.Should().Be("Avenue Louise 216");
		park.Capacity.Should().Be("350");
		park.DisabledCapacity.Should().BeNull();
		park.MaxHeight.Should().Be("1.9");
		park.Point.Latitude.Should().BeApproximately(50.84, 0.05);
	}

	/// <summary>
	/// Verifies that a car park request answered with an HTTP error is a failure that names the data.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCarParks_HttpError_ShouldFailWithUnavailable()
	{
		var result = await Mobility(StubHttpMessageHandler.Always(HttpStatusCode.BadGateway, "x")).GetCarParksAsync(Box, CancellationToken.None);

		result.Error.Code.Should().Be("Mobility.Unavailable");
		result.Error.Message.Should().Be("car parks HTTP 502");
	}
	#endregion

	#region Charging Point Tests
	/// <summary>
	/// Verifies the request and the reading of a charging point.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetChargers_Answer_ShouldSendBuiltAddressAndReadThePoint()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, Chargers);

		var result = await Mobility(handler).GetChargersAsync(Box, CancellationToken.None);

		handler.Requests.Should().Equal($"GET {SourceRequests.Chargers(Box)}");
		handler.Requests[0].Should().Contain("typeNames=bm_equipment:evse");
		var charger = result.Value.Should().ContainSingle().Subject;
		charger.Operator.Should().Be("Allego");
		charger.Points.Should().Be("2");
		charger.Power.Should().BeNull();
		charger.Standard.Should().Be("IEC 62196");
	}

	/// <summary>
	/// Verifies that a charging point answer that is not a feature collection is a BadResponse failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetChargers_NotAFeatureCollection_ShouldFailWithBadResponse()
	{
		var result = await Mobility(StubHttpMessageHandler.Always(HttpStatusCode.OK, "[]")).GetChargersAsync(Box, CancellationToken.None);

		result.Error.Code.Should().Be("Mobility.BadResponse");
		result.Error.Message.Should().Be("charging points: unexpected answer");
	}
	#endregion
}
