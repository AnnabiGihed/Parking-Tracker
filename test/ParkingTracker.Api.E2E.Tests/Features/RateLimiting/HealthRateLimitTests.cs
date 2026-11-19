using System.Net;
using ParkingTracker.ApiService.Features.RateLimiting;
using ParkingTracker.ServiceDefaults.Features.Health;

namespace ParkingTracker.Api.E2E.Tests.Features.RateLimiting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests of the per-address rate limit of the health endpoints on the real API composition. The client address comes from
///              X-Forwarded-For with the proxy key, as the shared proxy sends it. The liveness path is used because it needs no database.
/// </summary>
public sealed class HealthRateLimitTests
{
	#region Constants
	/// <summary>
	/// A connection string to a port nothing listens on; the liveness path never uses it.
	/// </summary>
	private const string UnreachableConnection = "Host=127.0.0.1;Port=1;Database=parking;Username=u;Password=p;Timeout=2";
	#endregion

	#region Rate Limit Tests
	/// <summary>
	/// Verifies that the request after the permitted number answers 429 with Retry-After and a problem document, while another
	/// address still gets 200.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Live_BeyondThePermitsOfOneAddress_ShouldAnswer429AndSpareOthers()
	{
		await using var factory = new ApiFactory("Production", UnreachableConnection);
		using var client = factory.CreateClient();
		for (var request = 0; request < RateLimitSettings.HealthPermitsPerWindow; request++)
		{
			(await GetLiveAsync(client, "203.0.113.1")).StatusCode.Should().Be(HttpStatusCode.OK);
		}

		var refused = await GetLiveAsync(client, "203.0.113.1");
		var other = await GetLiveAsync(client, "203.0.113.2");

		refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
		refused.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 60);
		refused.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
		other.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// Verifies that two IPv6 addresses of the same /64 share one bucket, and one of another /64 does not.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Live_IPv6AddressesOfOneNetwork_ShouldShareABucket()
	{
		await using var factory = new ApiFactory("Production", UnreachableConnection);
		using var client = factory.CreateClient();
		for (var request = 0; request < RateLimitSettings.HealthPermitsPerWindow; request++)
		{
			(await GetLiveAsync(client, $"2001:db8:1:2::{request + 1:x}")).StatusCode.Should().Be(HttpStatusCode.OK);
		}

		var sameNetwork = await GetLiveAsync(client, "2001:db8:1:2:ffff::1");
		var otherNetwork = await GetLiveAsync(client, "2001:db8:1:3::1");

		sameNetwork.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
		otherNetwork.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// Verifies that a forged address without the key does not escape the limit: it counts against the connection's own bucket.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Live_ForgedAddressesWithoutTheKey_ShouldShareTheConnectionBucket()
	{
		await using var factory = new ApiFactory("Production", UnreachableConnection);
		using var client = factory.CreateClient();
		for (var request = 0; request < RateLimitSettings.HealthPermitsPerWindow; request++)
		{
			(await GetLiveAsync(client, $"198.51.100.{request + 1}", key: null)).StatusCode.Should().Be(HttpStatusCode.OK);
		}

		var refused = await GetLiveAsync(client, "198.51.100.200", key: null);

		refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Requests the liveness path as the proxy would for a client address.
	/// </summary>
	/// <param name="client">The client of the API.</param>
	/// <param name="clientAddress">The address sent in X-Forwarded-For.</param>
	/// <param name="key">The proxy key header value; <see langword="null"/> for none.</param>
	/// <returns>The response.</returns>
	private static async Task<HttpResponseMessage> GetLiveAsync(HttpClient client, string clientAddress, string? key = ApiFactory.DefaultProxyKey)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, HealthEndpoints.LivePath);
		request.Headers.Add("X-Forwarded-For", clientAddress);
		if (key is not null)
		{
			request.Headers.Add("X-PT-Proxy-Key", key);
		}

		return await client.SendAsync(request).ConfigureAwait(false);
	}
	#endregion
}
