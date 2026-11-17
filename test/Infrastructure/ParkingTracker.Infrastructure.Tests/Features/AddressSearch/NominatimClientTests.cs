using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using ParkingTracker.Application.Features.AddressSearch.Abstractions;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Infrastructure.Features.AddressSearch;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.AddressSearch;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="NominatimClient"/> and the address search request over a stub HTTP handler: the parameters of
///              the first version, the reading of the best match, nothing found, the failure codes, and the headers and time limit
///              of the registered client.
/// </summary>
public sealed class NominatimClientTests
{
	#region Static Instances
	/// <summary>
	/// Timeouts short enough to be reached in a test.
	/// </summary>
	private static readonly SourceTimeouts ShortTimeouts = new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
	#endregion

	#region Address Search Tests
	/// <summary>
	/// Verifies that the search address keeps the parameters of the first version of the app and encodes the typed text.
	/// </summary>
	[Fact]
	public void AddressSearch_Text_ShouldKeepTheParametersOfTheFirstVersion()
	{
		SourceRequests.AddressSearch("Rue de la Loi 16").Should().Be(
			"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=be&viewbox=4.24,50.92,4.48,50.76&bounded=1&q=Rue%20de%20la%20Loi%2016");
	}

	/// <summary>
	/// Verifies that the best match is read from positions written as text.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_Match_ShouldReadThePosition()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, """[{"lat":"50.8503","lon":"4.3517","display_name":"Grand-Place"},{"display_name":"no position"}]""");

		var result = await new NominatimClient(new HttpClient(handler), SourceTimeouts.Default).SearchAsync("Grand-Place", CancellationToken.None);

		handler.Requests.Should().Equal($"GET {SourceRequests.AddressSearch("Grand-Place")}");
		var match = result.Value.Should().ContainSingle().Subject;
		match.Point.Should().Be(new GeoPoint(50.8503, 4.3517));
		match.DisplayName.Should().Be("Grand-Place");
	}

	/// <summary>
	/// Verifies that nothing found is an empty success, not a failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_NothingFound_ShouldSucceedEmpty()
	{
		var result = await new NominatimClient(new HttpClient(StubHttpMessageHandler.Always(HttpStatusCode.OK, "[]")), SourceTimeouts.Default).SearchAsync("zzz", CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies the failures: an object instead of an array, an HTTP error and a service that does not answer in time.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_Failures_ShouldGiveNominatimCodes()
	{
		var bad = await new NominatimClient(new HttpClient(StubHttpMessageHandler.Always(HttpStatusCode.OK, "{}")), SourceTimeouts.Default).SearchAsync("a", CancellationToken.None);
		var http = await new NominatimClient(new HttpClient(StubHttpMessageHandler.Always(HttpStatusCode.TooManyRequests, "")), SourceTimeouts.Default).SearchAsync("a", CancellationToken.None);
		var slow = await new NominatimClient(new HttpClient(StubHttpMessageHandler.Silent()), ShortTimeouts).SearchAsync("a", CancellationToken.None);

		bad.Error.Code.Should().Be("Nominatim.BadResponse");
		http.Error.Message.Should().Be("address search HTTP 429");
		slow.Error.Code.Should().Be("Nominatim.Timeout");
	}

	/// <summary>
	/// Verifies that the registered search client sends French as the answer language and the generic product name, and that the
	/// time limit registered for the source clients is 12 seconds.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Install_AddressSearch_ShouldSendFrenchAndGenericUserAgent()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "[]");
		var services = new ServiceCollection();
		new InfrastructureServiceInstaller().Install(services, new ConfigurationBuilder().Build());
		services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));
		using var provider = services.BuildServiceProvider(validateScopes: true);

		await provider.GetRequiredService<IAddressSearch>().SearchAsync("Rue Neuve", CancellationToken.None);

		handler.LastHeaders!.AcceptLanguage.ToString().Should().Be("fr");
		handler.LastHeaders.UserAgent.ToString().Should().Be("ParkingTracker/1.0");
		provider.GetRequiredService<SourceTimeouts>().Request.Should().Be(TimeSpan.FromSeconds(12));
	}
	#endregion
}
