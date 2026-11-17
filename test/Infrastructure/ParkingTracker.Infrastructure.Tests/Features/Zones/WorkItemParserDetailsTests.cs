using System.Net;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Zones;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests that the roadworks and event parsers keep what the map popups show (first day, who, link) next to what the
///              decisions use.
/// </summary>
public sealed class WorkItemParserDetailsTests
{
	#region Details Tests
	/// <summary>
	/// Verifies the first day, the pilot and the link of a roadworks site, and the first day and consequences of an event.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetWorks_SiteAndEvent_ShouldKeepPopupDetails()
	{
		var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(StubHttpMessageHandler.Text(HttpStatusCode.OK, request.RequestUri!.Query.Contains("bm_traffic", StringComparison.Ordinal)
			? """{"features":[{"geometry":{"type":"Point","coordinates":[485000,6590000]},"properties":{"is_active":true,"start_time":"2026-11-10T08:00:00.000Z","end_time":"2026-12-01T08:00:00.000Z","type_fr":"Manifestation","location_fr":"Rue Royale","consequences_fr":"Rue fermée"}}]}"""
			: """{"features":[{"geometry":{"type":"Point","coordinates":[485000,6590000]},"properties":{"str_start_date":"01/11/2026","str_end_date":"31/12/2026","nature_fr":"Travaux","road_impl_fr":"Rue Neuve","pilote_fr":"Bruxelles Mobilité","link_fr":"https://example.org/works"}}]}""")));
		var client = new MobilityLayersClient(new HttpClient(handler), SourceTimeouts.Default);

		var result = await client.GetWorksAsync(new GeoBox(50.8, 4.3, 50.9, 4.4), new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero), CancellationToken.None);

		var site = result.Value.Single(item => string.Equals(item.Title, "Travaux", StringComparison.Ordinal));
		site.From.Should().Be("2026-11-01");
		site.To.Should().Be("2026-12-31");
		site.By.Should().Be("Bruxelles Mobilité");
		site.Link.Should().Be("https://example.org/works");
		var evt = result.Value.Single(item => string.Equals(item.Title, "Manifestation", StringComparison.Ordinal));
		evt.From.Should().Be("2026-11-10");
		evt.By.Should().Be("Rue fermée");
		evt.Link.Should().BeEmpty();
	}
	#endregion
}
