using System.Net;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Infrastructure.Sources;
using ParkingTracker.Infrastructure.Tests.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for the data source clients (parking.brussels, UrbIS, parking rules, mobility layers) over a stub HTTP
///              handler: the exact requests sent, the hand-off to the parsers, and how a timeout, an HTTP error, an unreadable
///              answer, a lost connection and a cancellation come out.
/// </summary>
public sealed class SourceClientsTests
{
	#region Constants
	private const string EmptyCollection = """{"type":"FeatureCollection","features":[]}""";
	#endregion

	#region Static Instances
	private static readonly GeoBox Box = new(50.8243, 4.3591, 50.8257, 4.3621);
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
	private static readonly SourceTimeouts ShortTimeouts = new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
	#endregion

	#region Test Infrastructure
	private static HttpClient ClientFor(StubHttpMessageHandler handler) => new(handler);

	private static ParkingBrusselsClient ParkingBrussels(StubHttpMessageHandler handler, SourceTimeouts? timeouts = null) => new(ClientFor(handler), timeouts ?? SourceTimeouts.Default);
	#endregion

	#region Request Tests
	/// <summary>
	/// Verifies that each parking.brussels call sends one GET with the address the request builder gives.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ParkingBrussels_AllCalls_ShouldSendTheBuiltAddresses()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, EmptyCollection);
		var client = ParkingBrussels(handler);

		var zones = await client.GetZonesAsync(Box, CancellationToken.None);
		var spaces = await client.GetSpacesAsync(Box, CancellationToken.None);
		var mailles = await client.GetMaillesAsync(Box, CancellationToken.None);

		zones.IsSuccess.Should().BeTrue();
		spaces.IsSuccess.Should().BeTrue();
		mailles.IsSuccess.Should().BeTrue();
		handler.Requests.Should().Equal($"GET {SourceRequests.Zones(Box)}", $"GET {SourceRequests.Spaces(Box)}", $"GET {SourceRequests.Mailles(Box)}");
		handler.Requests[0].Should().Contain("typeNames=ParkingBrussels:regulation_zones").And.Contain("bbox=50.824300,4.359100,50.825700,4.362100,urn:ogc:def:crs:EPSG::4326");
	}

	/// <summary>
	/// Verifies the UrbIS request.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddressPoints_Box_ShouldSendTheBuiltAddress()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, EmptyCollection);

		var result = await new AddressPointsClient(ClientFor(handler), SourceTimeouts.Default).GetAddressPointsAsync(Box, CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		handler.Requests.Should().Equal($"GET {SourceRequests.AddressPoints(Box)}");
	}

	/// <summary>
	/// Verifies the rules request with and without a zone code.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Rules_WithAndWithoutCode_ShouldSendTheBuiltAddresses()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, EmptyCollection);
		var client = new ParkingRulesClient(ClientFor(handler), SourceTimeouts.Default);

		var one = await client.GetRulesAsync("A B", Now, CancellationToken.None);
		var all = await client.GetRulesAsync(null, Now, CancellationToken.None);

		one.Value.Should().BeEmpty();
		all.Value.Should().BeEmpty();
		handler.Requests.Should().Equal(
			"GET https://api.mobility.brussels/datasets/v1/parking/collections/regulations_rules/items/?f=json&limit=500&code=A%20B",
			"GET https://api.mobility.brussels/datasets/v1/parking/collections/regulations_rules/items/?f=json&limit=500");
	}

	/// <summary>
	/// Verifies that the roadworks and the traffic events are both asked for.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Works_BothLayersReadable_ShouldAskForBothAndSucceed()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, EmptyCollection);

		var result = await new MobilityLayersClient(ClientFor(handler), SourceTimeouts.Default).GetWorksAsync(Box, Now, CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		handler.Requests.Should().BeEquivalentTo($"GET {SourceRequests.Roadworks(Box)}", $"GET {SourceRequests.TrafficEvents(Box)}");
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that one unreadable layer makes the whole works answer a failure with that layer's message.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Works_EventsLayerFails_ShouldFailWithTheLayerMessage()
	{
		var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(request.RequestUri!.Query.Contains("bm_traffic", StringComparison.Ordinal)
			? StubHttpMessageHandler.Text(HttpStatusCode.InternalServerError, string.Empty)
			: StubHttpMessageHandler.Text(HttpStatusCode.OK, EmptyCollection)));

		var result = await new MobilityLayersClient(ClientFor(handler), SourceTimeouts.Default).GetWorksAsync(Box, Now, CancellationToken.None);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("Mobility.Unavailable");
		result.Error.Message.Should().Be("traffic events HTTP 500");
	}

	/// <summary>
	/// Verifies the HTTP error statuses: a failure coded Unavailable that names the status, with no retry.
	/// </summary>
	/// <param name="status">The HTTP status the service answers with.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(500)]
	[InlineData(404)]
	public async Task GetZones_HttpError_ShouldFailWithoutRetry(int status)
	{
		var handler = StubHttpMessageHandler.Always((HttpStatusCode)status, "oops");

		var result = await ParkingBrussels(handler).GetZonesAsync(Box, CancellationToken.None);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("ParkingBrussels.Unavailable");
		result.Error.Message.Should().Be($"parking.brussels HTTP {status}");
		handler.Requests.Should().HaveCount(1);
	}

	/// <summary>
	/// Verifies that an answer that is not JSON gives a BadResponse failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetAddressPoints_NotJson_ShouldFailWithBadResponse()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "<html>maintenance</html>");

		var result = await new AddressPointsClient(ClientFor(handler), SourceTimeouts.Default).GetAddressPointsAsync(Box, CancellationToken.None);

		result.Error.Code.Should().Be("Urbis.BadResponse");
		result.Error.Message.Should().Be("UrbIS: unexpected answer");
	}

	/// <summary>
	/// Verifies that JSON that is not a feature collection gives a BadResponse failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetRules_NoFeatures_ShouldFailWithBadResponse()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "{}");

		var result = await new ParkingRulesClient(ClientFor(handler), SourceTimeouts.Default).GetRulesAsync(null, Now, CancellationToken.None);

		result.Error.Code.Should().Be("ParkingRules.BadResponse");
	}

	/// <summary>
	/// Verifies that a service that does not answer in time gives a Timeout failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetZones_NoAnswerInTime_ShouldFailWithTimeout()
	{
		var result = await ParkingBrussels(StubHttpMessageHandler.Silent(), ShortTimeouts).GetZonesAsync(Box, CancellationToken.None);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("ParkingBrussels.Timeout");
		result.Error.Message.Should().Be("parking.brussels timed out");
	}

	/// <summary>
	/// Verifies that a lost connection gives an Unavailable failure and not an exception.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetZones_ConnectionFails_ShouldFailWithUnavailable()
	{
		var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("no route"));

		var result = await ParkingBrussels(handler).GetZonesAsync(Box, CancellationToken.None);

		result.Error.Code.Should().Be("ParkingBrussels.Unavailable");
		result.Error.Message.Should().Be("parking.brussels unreachable");
	}
	#endregion

	#region Cancellation Tests
	/// <summary>
	/// Verifies that a token cancelled before the call is rethrown, not turned into a failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetZones_CancelledBeforehand_ShouldThrowOperationCanceled()
	{
		using var source = new CancellationTokenSource();
		await source.CancelAsync();

		var act = () => ParkingBrussels(StubHttpMessageHandler.Silent()).GetZonesAsync(Box, source.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
	}

	/// <summary>
	/// Verifies that a cancellation by the caller during the call is rethrown even though a limit of time is set.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetZones_CancelledDuringTheCall_ShouldThrowOperationCanceled()
	{
		using var source = new CancellationTokenSource();
		var call = ParkingBrussels(StubHttpMessageHandler.Silent()).GetZonesAsync(Box, source.Token);

		await source.CancelAsync();

		await ((Func<Task>)(() => call)).Should().ThrowAsync<OperationCanceledException>();
	}
	#endregion
}
