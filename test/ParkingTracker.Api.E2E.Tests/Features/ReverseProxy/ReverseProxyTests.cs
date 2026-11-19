using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ParkingTracker.ApiService.Features.ReverseProxy;

namespace ParkingTracker.Api.E2E.Tests.Features.ReverseProxy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests of the trust in the shared reverse proxy (ADR-0029) on a small in-memory host that uses the same registration and
///              middleware order as the API and answers what the application sees: the connection's address, the scheme and the
///              headers. The host starts without a database, so the tests need no server.
/// </summary>
public sealed class ReverseProxyTests
{
	#region Constants
	/// <summary>
	/// The key the host is configured with.
	/// </summary>
	private const string Key = "the-shared-key";

	/// <summary>
	/// The client address the proxy forwards.
	/// </summary>
	private const string ForwardedAddress = "203.0.113.7";
	#endregion

	#region Forwarded Header Tests
	/// <summary>
	/// Verifies that the right key makes the forwarded address, scheme and host what the application sees.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Request_WithTheRightKey_ShouldApplyForwardedHeaders()
	{
		using var host = await StartAsync("Production", Key);

		var seen = await SendAsync(host, Key);

		seen.Should().Be($"{ForwardedAddress}|https|client.example|");
	}

	/// <summary>
	/// Verifies that a wrong key, a key of another length and no key leave the connection's own address and drop the forwarded headers.
	/// </summary>
	/// <param name="key">The key header value; <see langword="null"/> for no header.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("another-key")]
	[InlineData("x")]
	[InlineData("")]
	[InlineData(null)]
	public async Task Request_WithAWrongOrMissingKey_ShouldIgnoreForwardedHeaders(string? key)
	{
		using var host = await StartAsync("Production", Key);

		var seen = await SendAsync(host, key);

		seen.Should().Be("|http|localhost|");
	}

	/// <summary>
	/// Verifies that a request with the right key twice is not trusted: exactly one key header is accepted.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Request_WithTwoKeyHeaders_ShouldIgnoreForwardedHeaders()
	{
		using var host = await StartAsync("Production", Key);

		var seen = await SendAsync(host, [Key, Key]);

		seen.Should().Be("|http|localhost|");
	}

	/// <summary>
	/// Verifies that with the right key and a chain of addresses, only the last one, added by the proxy, is taken as the client.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Request_WithTheRightKeyAndAChain_ShouldTakeTheLastAddress()
	{
		using var host = await StartAsync("Production", Key);

		var seen = await SendAsync(host, [Key], "1.2.3.4, 5.6.7.8");

		seen.Should().Be("5.6.7.8|https|client.example|");
	}

	/// <summary>
	/// Verifies that the key header never reaches the application, whether it matched or not.
	/// </summary>
	/// <param name="key">The key header value.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(Key)]
	[InlineData("another-key")]
	public async Task Request_WithAKeyHeader_ShouldHideItFromTheApplication(string key)
	{
		using var host = await StartAsync("Production", Key);

		var seen = await SendAsync(host, key);

		seen.Should().EndWith("|");
	}

	/// <summary>
	/// Verifies that without a configured key, in Development, the forwarded headers are always ignored, even with a key header.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Request_NoKeyConfiguredInDevelopment_ShouldIgnoreForwardedHeaders()
	{
		using var host = await StartAsync("Development", string.Empty);

		var seen = await SendAsync(host, string.Empty);

		seen.Should().Be("|http|localhost|");
	}
	#endregion

	#region Start-up Tests
	/// <summary>
	/// Verifies that Production refuses to start without a proxy key, with a message that says what to set.
	/// </summary>
	/// <param name="key">The configured key; empty or blank for none.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("")]
	[InlineData("  ")]
	public async Task Start_ProductionWithoutAKey_ShouldFailWithAClearMessage(string key)
	{
		var act = () => StartAsync("Production", key);

		(await act.Should().ThrowAsync<OptionsValidationException>()).WithMessage("*ReverseProxy:Key*");
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Starts the small host: the proxy registration and middleware of the API, then an endpoint that echoes what the application sees.
	/// </summary>
	/// <param name="environment">The hosting environment.</param>
	/// <param name="key">The configured proxy key.</param>
	/// <returns>The started host.</returns>
	private static async Task<IHost> StartAsync(string environment, string key)
	{
		var host = new HostBuilder()
			.UseEnvironment(environment)
			.ConfigureWebHost(web => web
				.UseTestServer()
				.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
					new Dictionary<string, string?>(StringComparer.Ordinal) { [$"{ReverseProxyOptions.SectionName}:Key"] = key }))
				.ConfigureServices((context, services) => services.AddReverseProxyTrust(context.Configuration))
				.Configure(app =>
				{
					_ = app.UseReverseProxyTrust();
					app.Run(context => context.Response.WriteAsync(
						$"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}|{context.Request.Host}|{context.Request.Headers[ReverseProxyKeyMiddleware.KeyHeaderName]}"));
				}))
			.Build();
		await host.StartAsync().ConfigureAwait(false);
		return host;
	}

	/// <summary>
	/// Sends one request with forwarded headers and, when given, the key header, and returns what the application saw.
	/// </summary>
	/// <param name="host">The started host.</param>
	/// <param name="key">The key header value; <see langword="null"/> for no header.</param>
	/// <returns>The echo of the application.</returns>
	private static Task<string> SendAsync(IHost host, string? key)
	{
		return SendAsync(host, key is null ? [] : [key]);
	}

	/// <summary>
	/// Sends one request with forwarded headers and the given key header values, and returns what the application saw.
	/// </summary>
	/// <param name="host">The started host.</param>
	/// <param name="keys">The values of the key header; none for no header.</param>
	/// <param name="forwardedFor">The value of X-Forwarded-For.</param>
	/// <returns>The echo of the application.</returns>
	private static async Task<string> SendAsync(IHost host, string[] keys, string forwardedFor = ForwardedAddress)
	{
		using var client = host.GetTestClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, "/");
		request.Headers.Add("X-Forwarded-For", forwardedFor);
		request.Headers.Add("X-Forwarded-Proto", "https");
		request.Headers.Add("X-Forwarded-Host", "client.example");
		if (keys.Length > 0)
		{
			request.Headers.Add(ReverseProxyKeyMiddleware.KeyHeaderName, keys);
		}

		var response = await client.SendAsync(request).ConfigureAwait(false);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
	}
	#endregion
}
