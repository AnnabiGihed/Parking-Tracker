using System.Net;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Zones;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for the sector list of <see cref="ParkingBrusselsClient"/> over a stub HTTP handler: the request without a box
///              that asks for three properties only, and the entries without a zone code being left out.
/// </summary>
public sealed class ParkingBrusselsMailleListTests
{
	#region Constants
	/// <summary>
	/// The sector list: two entries and one without a zone code.
	/// </summary>
	private const string Mailles = """
		{"type":"FeatureCollection","features":[
		 {"type":"Feature","geometry":null,"properties":{"zonecode":"IXLR_1","namefre":"Ixelles 1"}},
		 {"type":"Feature","geometry":null,"properties":{"zonecode":"LIM_X","namefre":null}},
		 {"type":"Feature","geometry":null,"properties":{"namefre":"nothing"}}
		]}
		""";
	#endregion

	#region Sector List Tests
	/// <summary>
	/// Verifies that the sector list request carries no box and asks for the three properties only, and that entries without a
	/// zone code are left out.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMailleOptions_Answer_ShouldSendListAddressAndKeepCodedEntries()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, Mailles);

		var result = await new ParkingBrusselsClient(new HttpClient(handler), SourceTimeouts.Default).GetMailleOptionsAsync(CancellationToken.None);

		handler.Requests.Should().Equal($"GET {SourceRequests.MailleList()}");
		handler.Requests[0].Should().Contain("propertyName=zonecode,namefre,namedut").And.NotContain("bbox=");
		result.Value.Select(option => option.ZoneCode).Should().Equal("IXLR_1", "LIM_X");
		result.Value[1].DisplayName.Should().Be("LIM_X");
	}
	#endregion
}
