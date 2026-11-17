using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using ParkingTracker.Application.Features.Sectors.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.Shared.Http;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests that the installer registers the five source ports as typed clients of <c>IHttpClientFactory</c>, with
///              a generic User-Agent and a request that asks for no cached answer.
/// </summary>
public sealed class SourceRegistrationTests
{
	#region Test Infrastructure
	/// <summary>
	/// Builds a service provider whose source clients use the stub handler.
	/// </summary>
	/// <param name="handler">Stub handler that answers the requests.</param>
	/// <returns>Service provider.</returns>
	private static ServiceProvider ProviderWith(StubHttpMessageHandler handler)
	{
		var services = new ServiceCollection();
		new InfrastructureServiceInstaller().Install(services, new ConfigurationBuilder().Build());
		services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));
		return services.BuildServiceProvider(validateScopes: true);
	}
	#endregion

	#region Registration Tests
	/// <summary>
	/// Verifies that every source port resolves.
	/// </summary>
	[Fact]
	public void Install_ShouldRegisterEverySourcePort()
	{
		using var provider = ProviderWith(StubHttpMessageHandler.Always(HttpStatusCode.OK, "{}"));

		provider.GetRequiredService<IParkingBrusselsClient>().Should().NotBeNull();
		provider.GetRequiredService<IAddressPointsClient>().Should().NotBeNull();
		provider.GetRequiredService<IParkingRulesClient>().Should().NotBeNull();
		provider.GetRequiredService<IMobilityLayersClient>().Should().NotBeNull();
		provider.GetRequiredService<ICityDocumentsClient>().Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a request carries the generic product name and no personal data, and asks not to be answered from a cache.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Install_Client_ShouldSendGenericUserAgentAndNoCache()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, """{"features":[]}""");
		using var provider = ProviderWith(handler);

		await provider.GetRequiredService<IParkingBrusselsClient>().GetZonesAsync(new GeoBox(50.8, 4.3, 50.9, 4.4), CancellationToken.None);

		handler.LastHeaders!.UserAgent.ToString().Should().Be("ParkingTracker/1.0");
		handler.LastHeaders.CacheControl!.NoCache.Should().BeTrue();
		handler.LastHeaders.CacheControl.NoStore.Should().BeTrue();
	}
	#endregion
}
