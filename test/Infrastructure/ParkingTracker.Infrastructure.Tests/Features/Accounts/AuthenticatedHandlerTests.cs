using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Infrastructure.Features.Accounts;
using ParkingTracker.Infrastructure.Tests.Features.Accounts.TestDoubles;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests of the retry after a 401 for requests with a body: a small body is sent again with the same bytes and headers after one
///              refresh, while a body over the limit, or a stream that cannot be read twice, is sent once and its 401 returned as it is. Bodiless
///              requests keep their retry. The handler runs over the real refresher, a fake API and a fake token store.
/// </summary>
public sealed class AuthenticatedHandlerTests
{
	#region Constants
	/// <summary>
	/// The route of the refresh.
	/// </summary>
	private const string RefreshRoute = "POST /api/v1/sessions/refresh";

	/// <summary>
	/// The route of the profile.
	/// </summary>
	private const string ProfileRoute = "/api/v1/me/profile";

	/// <summary>
	/// The version of the profile the client sends in <c>If-Match</c>.
	/// </summary>
	private const string ProfileVersion = "\"v1\"";
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// The moment every test starts at.
	/// </summary>
	private static readonly DateTimeOffset Start = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// Builds a body of a given size whose length is known before it is sent.
	/// </summary>
	/// <param name="bytes">The size of the body, in bytes.</param>
	/// <returns>The body.</returns>
	private static StringContent TextOfSize(int bytes) => new(new string('a', bytes), Encoding.UTF8, "application/json");

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : What the fake API received for one request: the route, the bearer token, the body and the headers that must survive a retry.
	/// </summary>
	/// <param name="Route">The method and path.</param>
	/// <param name="Bearer">The bearer token sent, or <see langword="null"/>.</param>
	/// <param name="Body">The body sent, or <see langword="null"/> when there was none.</param>
	/// <param name="ContentType">The <c>Content-Type</c> sent, or <see langword="null"/>.</param>
	/// <param name="IfMatch">The <c>If-Match</c> sent, or <see langword="null"/>.</param>
	private sealed record Received(string Route, string? Bearer, string? Body, string? ContentType, string? IfMatch);

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Holds an HTTP client whose pipeline is the handler under test over the real refresher, the fake token store, and a fake API
	///              that answers the refresh and records every request it received.
	/// </summary>
	private sealed class Harness : IDisposable
	{
		#region Fields
		/// <summary>
		/// The service provider that holds the refresher and its HTTP client.
		/// </summary>
		private readonly ServiceProvider provider;

		/// <summary>
		/// The requests received, in order of arrival.
		/// </summary>
		private readonly ConcurrentQueue<Received> received = new();
		#endregion

		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="Harness"/> class with tokens that are valid for ten minutes.
		/// </summary>
		/// <param name="answerProfile">Answers every request that is not the refresh.</param>
		public Harness(Func<HttpRequestMessage, HttpResponseMessage> answerProfile)
		{
			var services = new ServiceCollection();
			services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
			services.AddSingleton<ISessionTokenStore>(Store);
			services.AddSingleton<IGoogleSignIn>(new FakeGoogleSignIn());
			new InfrastructureServiceInstaller().Install(services, new ConfigurationBuilder().Build());
			Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> server = async (request, cancellationToken) =>
			{
				var record = await RecordAsync(request, cancellationToken).ConfigureAwait(false);
				received.Enqueue(record);
				return string.Equals(record.Route, RefreshRoute, StringComparison.Ordinal) ? Refreshed() : answerProfile(request);
			};
			var apiHandler = new StubHttpMessageHandler(server);
			services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = apiHandler));
			provider = services.BuildServiceProvider(validateScopes: true);
			Store.Seed(new SessionTokens("access-1", Start.AddMinutes(10), "refresh-1"));
			var handler = new AuthenticatedHandler(provider.GetRequiredService<AccessTokenRefresher>()) { InnerHandler = new StubHttpMessageHandler(server) };
			Client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
		}
		#endregion

		#region Properties
		/// <summary>
		/// Gets the HTTP client whose pipeline is the handler under test.
		/// </summary>
		public HttpClient Client { get; }

		/// <summary>
		/// Gets the fake token store.
		/// </summary>
		public InMemorySessionTokenStore Store { get; } = new();

		/// <summary>
		/// Gets the requests received so far, in order of arrival.
		/// </summary>
		public IReadOnlyList<Received> Requests => [.. received];
		#endregion

		#region IDisposable Implementation
		/// <inheritdoc />
		public void Dispose()
		{
			Client.Dispose();
			provider.Dispose();
		}
		#endregion

		#region Public Methods
		/// <summary>
		/// Counts the requests to a route.
		/// </summary>
		/// <param name="route">The method and path.</param>
		/// <returns>How many requests the route received.</returns>
		public int CountOf(string route) => received.Count(entry => string.Equals(entry.Route, route, StringComparison.Ordinal));
		#endregion

		#region Private Helpers
		/// <summary>
		/// Builds the answer of a successful refresh.
		/// </summary>
		/// <returns>The 200 answer with the second pair of tokens.</returns>
		private static HttpResponseMessage Refreshed()
		{
			var body = JsonSerializer.Serialize(new { accessToken = "access-2", accessTokenExpiresAt = Start.AddMinutes(15), refreshToken = "refresh-2" });
			return StubHttpMessageHandler.Text(HttpStatusCode.OK, body);
		}

		/// <summary>
		/// Reads what a request carries, including its body, while it is being sent.
		/// </summary>
		/// <param name="request">The request.</param>
		/// <param name="cancellationToken">A token to cancel the reading of the body.</param>
		/// <returns>A task with the record of the request.</returns>
		private static async Task<Received> RecordAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var uri = request.RequestUri ?? throw new InvalidOperationException("The test request must have a URI.");
			var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var ifMatch = request.Headers.TryGetValues("If-Match", out var values) ? string.Join(',', values) : null;
			return new Received(
				$"{request.Method} {uri.AbsolutePath}",
				request.Headers.Authorization?.Parameter,
				body,
				request.Content?.Headers.ContentType?.ToString(),
				ifMatch);
		}
		#endregion
	}

	/// <summary>
	/// Builds a server that refuses the first access token and accepts the refreshed one.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <returns>A 204 for the refreshed token, a 401 otherwise.</returns>
	private static HttpResponseMessage AcceptOnlyRefreshedToken(HttpRequestMessage request)
	{
		var accepted = string.Equals(request.Headers.Authorization?.Parameter, "access-2", StringComparison.Ordinal);
		return new HttpResponseMessage(accepted ? HttpStatusCode.NoContent : HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// Builds a server that refuses every access token.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <returns>A 401.</returns>
	private static HttpResponseMessage RefuseEveryToken(HttpRequestMessage request) => new(HttpStatusCode.Unauthorized) { RequestMessage = request };
	#endregion

	#region Body Retry Tests
	/// <summary>
	/// Verifies that a PUT with a JSON body that is answered 401 is refreshed once and sent again with the same body, content type and <c>If-Match</c>.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SendAsync_PutWithJsonBodyAnswered401_ShouldRetryOnceWithTheSameBodyAndHeaders()
	{
		using var harness = new Harness(AcceptOnlyRefreshedToken);
		using var request = new HttpRequestMessage(HttpMethod.Put, ProfileRoute) { Content = JsonContent.Create(new { home = "Rue de la Loi 16", vehicle = "1-ABC-123" }) };
		request.Headers.TryAddWithoutValidation("If-Match", ProfileVersion).Should().BeTrue();

		using var response = await harness.Client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		harness.CountOf(RefreshRoute).Should().Be(1);
		var puts = harness.Requests.Where(entry => string.Equals(entry.Route, $"PUT {ProfileRoute}", StringComparison.Ordinal)).ToList();
		puts.Select(entry => entry.Bearer).Should().Equal("access-1", "access-2");
		puts[0].Body.Should().Contain("Rue de la Loi 16");
		puts[1].Should().Be(puts[0] with { Bearer = "access-2" });
		puts[1].ContentType.Should().StartWith("application/json");
		puts[1].IfMatch.Should().Be(ProfileVersion);
	}

	/// <summary>
	/// Verifies that a body of exactly the limit is still retried.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SendAsync_BodyAtTheLimitAnswered401_ShouldRetryWithTheSameBody()
	{
		using var harness = new Harness(AcceptOnlyRefreshedToken);
		using var request = new HttpRequestMessage(HttpMethod.Put, ProfileRoute) { Content = TextOfSize(AuthenticatedHandler.MaxRetryBodyBytes) };

		using var response = await harness.Client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		var puts = harness.Requests.Where(entry => entry.Route.StartsWith("PUT", StringComparison.Ordinal)).ToList();
		puts.Should().HaveCount(2);
		puts[1].Body.Should().HaveLength(AuthenticatedHandler.MaxRetryBodyBytes);
	}

	/// <summary>
	/// Verifies that a body whose known length is over the limit is sent once, in full, and its 401 is returned without a refresh.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SendAsync_BodyOverTheLimitAnswered401_ShouldReturnThe401WithoutRetry()
	{
		using var harness = new Harness(RefuseEveryToken);
		using var request = new HttpRequestMessage(HttpMethod.Put, ProfileRoute) { Content = TextOfSize(AuthenticatedHandler.MaxRetryBodyBytes + 1) };

		using var response = await harness.Client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		harness.CountOf(RefreshRoute).Should().Be(0);
		var put = harness.Requests.Should().ContainSingle().Subject;
		put.Body.Should().HaveLength(AuthenticatedHandler.MaxRetryBodyBytes + 1);
	}

	/// <summary>
	/// Verifies that a JSON body of unknown length that turns out to be over the limit is still sent once in full, and its 401 is returned without a refresh.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SendAsync_JsonBodyOverTheLimitAnswered401_ShouldSendItOnceInFullWithoutRetry()
	{
		using var harness = new Harness(RefuseEveryToken);
		var note = new string('n', AuthenticatedHandler.MaxRetryBodyBytes);
		using var request = new HttpRequestMessage(HttpMethod.Put, ProfileRoute) { Content = JsonContent.Create(new { note }) };

		using var response = await harness.Client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		harness.CountOf(RefreshRoute).Should().Be(0);
		var put = harness.Requests.Should().ContainSingle().Subject;
		put.Body.Should().Contain(note);
	}

	/// <summary>
	/// Verifies that a stream that cannot be rewound is sent once, in full, and its 401 is returned without a refresh.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SendAsync_ForwardOnlyStreamAnswered401_ShouldSendItOnceWithoutRetry()
	{
		using var harness = new Harness(RefuseEveryToken);
		using var stream = new ForwardOnlyStream(Encoding.UTF8.GetBytes("""{"home":"Rue de la Loi 16"}"""));
		using var request = new HttpRequestMessage(HttpMethod.Put, ProfileRoute) { Content = new StreamContent(stream) };

		using var response = await harness.Client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		harness.CountOf(RefreshRoute).Should().Be(0);
		harness.Requests.Should().ContainSingle().Which.Body.Should().Be("""{"home":"Rue de la Loi 16"}""");
	}

	/// <summary>
	/// Verifies that a body answered 401 twice is retried once only, after a single refresh, and the second 401 is returned.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SendAsync_BodyStillRefusedAfterRefresh_ShouldStopAfterOneRetry()
	{
		using var harness = new Harness(RefuseEveryToken);
		using var request = new HttpRequestMessage(HttpMethod.Put, ProfileRoute) { Content = JsonContent.Create(new { home = "Rue de la Loi 16" }) };

		using var response = await harness.Client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		harness.CountOf(RefreshRoute).Should().Be(1);
		harness.CountOf($"PUT {ProfileRoute}").Should().Be(2);
	}
	#endregion

	#region Bodiless Retry Tests
	/// <summary>
	/// Verifies that a request without a body is still refreshed once and sent again with the new token after a 401.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SendAsync_GetAnswered401_ShouldRetryOnceWithTheNewToken()
	{
		using var harness = new Harness(AcceptOnlyRefreshedToken);
		using var request = new HttpRequestMessage(HttpMethod.Get, ProfileRoute);

		using var response = await harness.Client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		harness.Requests.Select(entry => $"{entry.Route} {entry.Bearer}").Should().Equal($"GET {ProfileRoute} access-1", RefreshRoute + " ", $"GET {ProfileRoute} access-2");
		harness.Requests.Should().OnlyContain(entry => entry.Route == RefreshRoute || entry.Body == null);
	}

	/// <summary>
	/// Verifies that a request without a body that is accepted is sent once, without a refresh.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SendAsync_GetAccepted_ShouldSendOnceWithoutRefresh()
	{
		using var harness = new Harness(_ => new HttpResponseMessage(HttpStatusCode.OK));
		using var request = new HttpRequestMessage(HttpMethod.Get, ProfileRoute);

		using var response = await harness.Client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		harness.Requests.Should().ContainSingle().Which.Bearer.Should().Be("access-1");
	}
	#endregion
}
