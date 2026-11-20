using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;
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
///              the first version with the limit and the address details, the reading of the matches with their label and locality
///              (also from a recorded answer with several results), nothing found, the failure codes, the repeated question and the
///              pace of the requests (on a fake clock), and the headers and time limit of the registered client.
/// </summary>
public sealed class NominatimClientTests
{
	#region Constants
	/// <summary>
	/// The recorded answer of the service for a search with several results, from the repository root.
	/// </summary>
	private const string SeveralMatchesFixture = "test/fixtures/nominatim/search-several-matches.json";
	#endregion

	#region Static Instances
	/// <summary>
	/// Timeouts short enough to be reached in a test.
	/// </summary>
	private static readonly SourceTimeouts ShortTimeouts = new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
	#endregion

	#region Address Search Tests
	/// <summary>
	/// Verifies that the search address keeps the country, the box and the bounding of the first version of the app, asks for the
	/// address details and for eight matches by default, and encodes the typed text.
	/// </summary>
	[Fact]
	public void AddressSearch_Text_ShouldAskForEightMatchesWithDetails()
	{
		SourceRequests.AddressSearch("Rue de la Loi 16").Should().Be(
			"https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=8&countrycodes=be&viewbox=4.24,50.92,4.48,50.76&bounded=1&q=Rue%20de%20la%20Loi%2016");
	}

	/// <summary>
	/// Verifies that the limit of the caller is in the request.
	/// </summary>
	[Fact]
	public void AddressSearch_Limit_ShouldBeInTheRequest()
	{
		SourceRequests.AddressSearch("Rue Neuve", 3).Should().Contain("&limit=3&").And.Contain("addressdetails=1");
	}

	/// <summary>
	/// Verifies that a match is read from positions written as text, with the label of its name and no locality when the details are
	/// missing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_Match_ShouldReadThePosition()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, """[{"lat":"50.8503","lon":"4.3517","display_name":"Grand-Place, Bruxelles","name":"Grand-Place"},{"display_name":"no position"}]""");
		using var gate = NewGate();

		var result = await NewClient(handler, gate).SearchAsync("Grand-Place", 5, CancellationToken.None);

		handler.Requests.Should().Equal($"GET {SourceRequests.AddressSearch("Grand-Place", 5)}");
		var match = result.Value.Should().ContainSingle().Subject;
		match.Point.Should().Be(new GeoPoint(50.8503, 4.3517));
		match.DisplayName.Should().Be("Grand-Place, Bruxelles");
		match.Label.Should().Be("Grand-Place");
		match.Locality.Should().BeNull();
	}

	/// <summary>
	/// Verifies the label and the locality of each kind of result of a recorded answer: a road, a road with a house number, a named
	/// place, a result with only a full name, and an entry without a position that is left out.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_SeveralResults_ShouldBuildLabelAndLocality()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, await File.ReadAllTextAsync(FixturePath(SeveralMatchesFixture)));
		using var gate = NewGate();

		var result = await NewClient(handler, gate).SearchAsync("Loi", 8, CancellationToken.None);

		result.Value.Select(match => (match.Label, match.Locality)).Should().Equal(
			("Rue de la Loi", "1000 Bruxelles"),
			("Rue de la Loi 16", "1040 Etterbeek"),
			("Grand-Place", "1000 Bruxelles"),
			("Parc de Forest", null));
	}

	/// <summary>
	/// Verifies that nothing found is an empty success, not a failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_NothingFound_ShouldSucceedEmpty()
	{
		using var gate = NewGate();

		var result = await NewClient(StubHttpMessageHandler.Always(HttpStatusCode.OK, "[]"), gate).SearchAsync("zzz", 8, CancellationToken.None);

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
		using var badGate = NewGate();
		using var httpGate = NewGate();
		using var slowGate = NewGate();

		var bad = await NewClient(StubHttpMessageHandler.Always(HttpStatusCode.OK, "{}"), badGate).SearchAsync("a", 8, CancellationToken.None);
		var http = await NewClient(StubHttpMessageHandler.Always(HttpStatusCode.TooManyRequests, string.Empty), httpGate).SearchAsync("a", 8, CancellationToken.None);
		var slow = await new NominatimClient(new HttpClient(StubHttpMessageHandler.Silent()), ShortTimeouts, slowGate).SearchAsync("a", 8, CancellationToken.None);

		bad.Error.Code.Should().Be("Nominatim.BadResponse");
		http.Error.Message.Should().Be("address search HTTP 429");
		slow.Error.Code.Should().Be("Nominatim.Timeout");
	}

	/// <summary>
	/// Verifies that a question that was answered is answered again from memory, whatever its case, without a second request.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_SameQuestionTwice_ShouldSendOneRequest()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, """[{"lat":"50.85","lon":"4.35","display_name":"Rue Neuve"}]""");
		using var gate = NewGate();
		var client = NewClient(handler, gate);

		var first = await client.SearchAsync("Rue Neuve", 8, CancellationToken.None);
		var second = await client.SearchAsync("rue neuve", 8, CancellationToken.None);

		handler.Requests.Should().HaveCount(1);
		second.Value.Should().Equal(first.Value);
	}

	/// <summary>
	/// Verifies that a failure is not remembered: asking again sends the request again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_Failure_ShouldNotBeRemembered()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.TooManyRequests, string.Empty);
		var clock = new FakeTimeProvider();
		using var gate = NewGate(clock);
		var client = NewClient(handler, gate);

		await client.SearchAsync("a", 8, CancellationToken.None);
		clock.Advance(TimeSpan.FromSeconds(1));
		await client.SearchAsync("a", 8, CancellationToken.None);

		handler.Requests.Should().HaveCount(2);
	}

	/// <summary>
	/// Verifies that a second, different question waits until a second has passed since the first request, as the usage policy of
	/// the service asks, and is then sent.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Search_TwoQuestions_ShouldWaitASecondBetweenRequests()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.OK, "[]");
		var clock = new FakeTimeProvider();
		using var gate = NewGate(clock);
		var client = NewClient(handler, gate);
		await client.SearchAsync("one", 8, CancellationToken.None);

		var second = client.SearchAsync("two", 8, CancellationToken.None);

		second.IsCompleted.Should().BeFalse();
		handler.Requests.Should().HaveCount(1);
		clock.Advance(TimeSpan.FromSeconds(1));
		await second;
		handler.Requests.Should().HaveCount(2);
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

		await provider.GetRequiredService<IAddressSearch>().SearchAsync("Rue Neuve", 8, CancellationToken.None);

		handler.LastHeaders!.AcceptLanguage.ToString().Should().Be("fr");
		handler.LastHeaders.UserAgent.ToString().Should().Be("ParkingTracker/1.0");
		provider.GetRequiredService<SourceTimeouts>().Request.Should().Be(TimeSpan.FromSeconds(12));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a search client over a stub handler and the gate of the test, so that no test sees the memory of another.
	/// </summary>
	/// <param name="handler">The stub that answers the requests.</param>
	/// <param name="gate">The gate of the client.</param>
	/// <returns>The client.</returns>
	private static NominatimClient NewClient(StubHttpMessageHandler handler, AddressSearchGate gate)
	{
		return new NominatimClient(new HttpClient(handler), SourceTimeouts.Default, gate);
	}

	/// <summary>
	/// Builds a gate on a clock.
	/// </summary>
	/// <param name="clock">The clock, or <see langword="null"/> for the system clock.</param>
	/// <returns>The gate.</returns>
	private static AddressSearchGate NewGate(TimeProvider? clock = null) => new(clock ?? TimeProvider.System);

	/// <summary>
	/// Looks for a file of the repository above the test output.
	/// </summary>
	/// <param name="relativePath">The path from the repository root.</param>
	/// <returns>The full path of the file.</returns>
	private static string FixturePath(string relativePath)
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			var candidate = Path.Combine(directory.FullName, relativePath);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new FileNotFoundException($"{relativePath} was not found above {AppContext.BaseDirectory}.");
	}
	#endregion
}
