using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Infrastructure.Tests.Features.Accounts.TestDoubles;
using ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests of the session client over the real registration (the installer, the typed clients and the authentication handler) with a fake
///              API, a fake token store and a fake clock: the sign-in flow stores the tokens, an expired or refused access token is refreshed and the request
///              is retried once, concurrent callers share one refresh, only a rejected refresh signs the user out, a network error never does, sign-out
///              and deletion clear the session, and no token appears in a failure message.
/// </summary>
public sealed class SessionClientTests
{
	#region Constants
	/// <summary>
	/// The route of the challenge.
	/// </summary>
	private const string ChallengeRoute = "POST /api/v1/sessions/challenges";

	/// <summary>
	/// The route of the exchange.
	/// </summary>
	private const string ExchangeRoute = "POST /api/v1/sessions";

	/// <summary>
	/// The route of the refresh.
	/// </summary>
	private const string RefreshRoute = "POST /api/v1/sessions/refresh";

	/// <summary>
	/// The route of the account.
	/// </summary>
	private const string MeRoute = "GET /api/v1/me";

	/// <summary>
	/// The route of the sign-out.
	/// </summary>
	private const string SignOutRoute = "DELETE /api/v1/sessions/current";

	/// <summary>
	/// The route of the deletion of the account.
	/// </summary>
	private const string DeleteRoute = "DELETE /api/v1/me";

	/// <summary>
	/// The body of the account.
	/// </summary>
	private const string MeBody = """{"accountId":"0f3c2a1b-8d4e-4f60-9a7b-123456789abc","createdOn":"2026-11-03","lastSeenOn":"2026-11-17","activeSessionCount":2}""";
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// The moment every test starts at.
	/// </summary>
	private static readonly DateTimeOffset Start = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// Tokens that are valid for ten minutes.
	/// </summary>
	private static readonly SessionTokens Valid = new("access-1", Start.AddMinutes(10), "refresh-1");

	/// <summary>
	/// Tokens whose access token ended a minute ago.
	/// </summary>
	private static readonly SessionTokens Expired = new("access-1", Start.AddMinutes(-1), "refresh-1");

	/// <summary>
	/// Builds the key of a request: its method and path.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <returns>For example <c>GET /api/v1/me</c>.</returns>
	private static string Route(HttpRequestMessage request)
	{
		var uri = request.RequestUri ?? throw new InvalidOperationException("The test request must have a URI.");
		return $"{request.Method} {uri.AbsolutePath}";
	}

	/// <summary>
	/// Tells whether a request is for a route.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <param name="route">The method and path.</param>
	/// <returns><see langword="true"/> when the request is for the route.</returns>
	private static bool IsRoute(HttpRequestMessage request, string route) => string.Equals(Route(request), route, StringComparison.Ordinal);

	/// <summary>
	/// Tells whether a request carries a bearer token.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <param name="token">The expected token.</param>
	/// <returns><see langword="true"/> when the request carries that token.</returns>
	private static bool HasBearer(HttpRequestMessage request, string token) => string.Equals(request.Headers.Authorization?.Parameter, token, StringComparison.Ordinal);

	/// <summary>
	/// Builds the answer of a successful sign-in or refresh.
	/// </summary>
	/// <param name="access">The access token.</param>
	/// <param name="refresh">The refresh token.</param>
	/// <returns>The 200 answer.</returns>
	private static HttpResponseMessage Session(string access, string refresh)
	{
		var body = JsonSerializer.Serialize(new { accessToken = access, accessTokenExpiresAt = Start.AddMinutes(15), refreshToken = refresh });
		return StubHttpMessageHandler.Text(HttpStatusCode.OK, body);
	}

	/// <summary>
	/// Builds an answer with only a status.
	/// </summary>
	/// <param name="status">The status.</param>
	/// <returns>The answer.</returns>
	private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Holds the client over the real registration, the fakes and a record of what the fake API received.
	/// </summary>
	private sealed class Harness : IDisposable
	{
		#region Fields
		/// <summary>
		/// The service provider that holds the registration.
		/// </summary>
		private readonly ServiceProvider provider;

		/// <summary>
		/// The requests received, as method, path and the bearer token that was sent.
		/// </summary>
		private readonly ConcurrentQueue<string> received = new();
		#endregion

		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="Harness"/> class.
		/// </summary>
		/// <param name="server">Answers each request of the client; a throw is a network error.</param>
		public Harness(Func<HttpRequestMessage, Task<HttpResponseMessage>> server)
		{
			var services = new ServiceCollection();
			services.AddSingleton<TimeProvider>(Clock);
			services.AddSingleton<ISessionTokenStore>(Store);
			services.AddSingleton<IGoogleSignIn>(Google);
			new InfrastructureServiceInstaller().Install(services, new ConfigurationBuilder().Build());
			var handler = new StubHttpMessageHandler((request, _) =>
			{
				received.Enqueue($"{Route(request)} {request.Headers.Authorization?.Parameter}".TrimEnd());
				return server(request);
			});
			services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));
			provider = services.BuildServiceProvider(validateScopes: true);
			Client = provider.GetRequiredService<ISessionClient>();
		}
		#endregion

		#region Properties
		/// <summary>
		/// Gets the client under test.
		/// </summary>
		public ISessionClient Client { get; }

		/// <summary>
		/// Gets the fake clock.
		/// </summary>
		public FakeTimeProvider Clock { get; } = new(Start);

		/// <summary>
		/// Gets the fake token store.
		/// </summary>
		public InMemorySessionTokenStore Store { get; } = new();

		/// <summary>
		/// Gets the fake Google sign-in.
		/// </summary>
		public FakeGoogleSignIn Google { get; } = new();

		/// <summary>
		/// Gets the requests received so far, as method, path and the bearer token sent.
		/// </summary>
		public IReadOnlyList<string> Received => [.. received];
		#endregion

		#region IDisposable Implementation
		/// <inheritdoc />
		public void Dispose() => provider.Dispose();
		#endregion

		#region Public Methods
		/// <summary>
		/// Counts the requests to a route.
		/// </summary>
		/// <param name="route">The method and path.</param>
		/// <returns>How many requests the route received.</returns>
		public int CountOf(string route) => received.Count(entry => string.Equals(entry, route, StringComparison.Ordinal) || entry.StartsWith(route + " ", StringComparison.Ordinal));
		#endregion
	}

	/// <summary>
	/// Builds a harness over a server that answers synchronously.
	/// </summary>
	/// <param name="server">Answers each request.</param>
	/// <returns>The harness.</returns>
	private static Harness Arrange(Func<HttpRequestMessage, HttpResponseMessage> server) => new(request => Task.FromResult(server(request)));
	#endregion

	#region Sign In Tests
	/// <summary>
	/// Verifies that a sign-in asks for a nonce, gives it to Google, exchanges the ID token and stores the tokens.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_Succeeds_ShouldExchangeTheIdTokenAndStoreTheTokens()
	{
		string? exchanged = null;
		using var harness = new Harness(async request =>
		{
			switch (Route(request))
			{
				case ChallengeRoute:
					return StubHttpMessageHandler.Text(HttpStatusCode.OK, """{"nonce":"nonce-1","expiresAt":"2026-11-17T10:05:00+00:00"}""");
				case ExchangeRoute:
					var content = request.Content ?? throw new InvalidOperationException("The exchange request must have content.");
					exchanged = await content.ReadAsStringAsync().ConfigureAwait(false);
					return Session("access-1", "refresh-1");
				default:
					return Status(HttpStatusCode.NotFound);
			}
		});

		var result = await harness.Client.SignInAsync();

		result.IsSuccess.Should().BeTrue();
		harness.Google.LastNonce.Should().Be("nonce-1");
		exchanged.Should().Contain("google-id-token");
		harness.Store.Current.Should().Be(new SessionTokens("access-1", Start.AddMinutes(15), "refresh-1"));
		(await harness.Client.IsSignedInAsync()).Should().BeTrue();
		harness.Received.Should().Equal(ChallengeRoute, ExchangeRoute);
	}

	/// <summary>
	/// Verifies that each Google failure is passed on as it is, and that nothing is exchanged or stored.
	/// </summary>
	/// <param name="code">The code of the Google failure.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(AccountErrors.CancelledCode)]
	[InlineData(AccountErrors.NoAccountCode)]
	[InlineData(AccountErrors.UnavailableCode)]
	public async Task SignInAsync_GoogleFails_ShouldPassTheFailureOnAndStoreNothing(string code)
	{
		using var harness = Arrange(request => IsRoute(request, ChallengeRoute)
			? StubHttpMessageHandler.Text(HttpStatusCode.OK, """{"nonce":"nonce-1","expiresAt":"2026-11-17T10:05:00+00:00"}""")
			: Status(HttpStatusCode.NotFound));
		harness.Google.Answer = Result.Failure<string>(new Error(code, "reason"));

		var result = await harness.Client.SignInAsync();

		result.Error.Code.Should().Be(code);
		harness.Store.Current.Should().BeNull();
		harness.CountOf(ExchangeRoute).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a build without Google sign-in fails as unavailable before it calls the API.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_GoogleNotAvailable_ShouldFailWithoutCallingTheApi()
	{
		using var harness = Arrange(_ => Status(HttpStatusCode.NotFound));
		harness.Google.IsAvailable = false;

		var result = await harness.Client.SignInAsync();

		result.Error.Code.Should().Be(AccountErrors.UnavailableCode);
		harness.Received.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that an ID token the server refuses is reported as refused and stores nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_ExchangeRefused_ShouldFailAsRefusedAndStoreNothing()
	{
		using var harness = Arrange(request => IsRoute(request, ChallengeRoute)
			? StubHttpMessageHandler.Text(HttpStatusCode.OK, """{"nonce":"nonce-1","expiresAt":"2026-11-17T10:05:00+00:00"}""")
			: Status(HttpStatusCode.Unauthorized));

		var result = await harness.Client.SignInAsync();

		result.Error.Code.Should().Be(AccountErrors.RefusedCode);
		harness.Store.Current.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a server that cannot be reached is reported as a network failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignInAsync_ServerUnreachable_ShouldFailAsNetwork()
	{
		using var harness = new Harness(_ => throw new HttpRequestException("no route"));

		var result = await harness.Client.SignInAsync();

		result.Error.Code.Should().Be(AccountErrors.NetworkCode);
		harness.Google.LastNonce.Should().BeNull();
	}
	#endregion

	#region Authenticated Call Tests
	/// <summary>
	/// Verifies that the account is read with the bearer token and without a refresh while the token is valid.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_ValidToken_ShouldSendTheBearerAndNotRefresh()
	{
		using var harness = Arrange(_ => StubHttpMessageHandler.Text(HttpStatusCode.OK, MeBody));
		harness.Store.Seed(Valid);

		var result = await harness.Client.GetMeAsync();

		result.IsSuccess.Should().BeTrue();
		result.Value.ActiveSessionCount.Should().Be(2);
		result.Value.CreatedOn.Should().Be(new DateOnly(2026, 11, 3));
		harness.Received.Should().Equal("GET /api/v1/me access-1");
	}

	/// <summary>
	/// Verifies that without a session no request is sent and the call fails as not signed in.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_NoSession_ShouldFailAsNotSignedInWithoutARequest()
	{
		using var harness = Arrange(_ => StubHttpMessageHandler.Text(HttpStatusCode.OK, MeBody));

		var result = await harness.Client.GetMeAsync();

		result.Error.Code.Should().Be(AccountErrors.NotSignedInCode);
		harness.Received.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that an access token about to expire is refreshed before the request, and the new one is stored and sent.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_ExpiredToken_ShouldRefreshFirstAndSendTheNewToken()
	{
		using var harness = Arrange(request => IsRoute(request, RefreshRoute) ? Session("access-2", "refresh-2") : StubHttpMessageHandler.Text(HttpStatusCode.OK, MeBody));
		harness.Store.Seed(Expired);

		var result = await harness.Client.GetMeAsync();

		result.IsSuccess.Should().BeTrue();
		harness.Received.Should().Equal(RefreshRoute, "GET /api/v1/me access-2");
		var tokens = harness.Store.Current ?? throw new InvalidOperationException("The refreshed tokens must be stored.");
		tokens.RefreshToken.Should().Be("refresh-2");
	}

	/// <summary>
	/// Verifies that a 401 refreshes the tokens and retries the request once with the new token.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_Answered401_ShouldRefreshAndRetryOnce()
	{
		using var harness = Arrange(request => Route(request) switch
		{
			RefreshRoute => Session("access-2", "refresh-2"),
			MeRoute when HasBearer(request, "access-2") => StubHttpMessageHandler.Text(HttpStatusCode.OK, MeBody),
			_ => Status(HttpStatusCode.Unauthorized),
		});
		harness.Store.Seed(Valid);

		var result = await harness.Client.GetMeAsync();

		result.IsSuccess.Should().BeTrue();
		harness.Received.Should().Equal("GET /api/v1/me access-1", RefreshRoute, "GET /api/v1/me access-2");
	}

	/// <summary>
	/// Verifies that a second 401 after the refresh is not retried again: the call fails as not signed in, the tokens stay and the refresh ran once.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_StillRefusedAfterRefresh_ShouldStopAfterOneRetry()
	{
		using var harness = Arrange(request => IsRoute(request, RefreshRoute) ? Session("access-2", "refresh-2") : Status(HttpStatusCode.Unauthorized));
		harness.Store.Seed(Valid);

		var result = await harness.Client.GetMeAsync();

		result.Error.Code.Should().Be(AccountErrors.NotSignedInCode);
		harness.CountOf("GET /api/v1/me").Should().Be(2);
		harness.CountOf(RefreshRoute).Should().Be(1);
		harness.Store.Current.Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that a refresh the API rejects signs the user out: the tokens are deleted and the call fails as not signed in.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_RefreshRejected_ShouldDeleteTheTokensAndFailAsNotSignedIn()
	{
		using var harness = Arrange(_ => Status(HttpStatusCode.Unauthorized));
		harness.Store.Seed(Valid);

		var result = await harness.Client.GetMeAsync();

		result.Error.Code.Should().Be(AccountErrors.NotSignedInCode);
		harness.Store.Current.Should().BeNull();
		(await harness.Client.IsSignedInAsync()).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a network error during the refresh after a 401 never signs the user out: the tokens stay and the call fails as a network failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_RefreshHitsNetworkError_ShouldKeepTheTokens()
	{
		using var harness = new Harness(request => IsRoute(request, RefreshRoute)
			? throw new HttpRequestException("connection lost")
			: Task.FromResult(Status(HttpStatusCode.Unauthorized)));
		harness.Store.Seed(Valid);

		var result = await harness.Client.GetMeAsync();

		result.Error.Code.Should().Be(AccountErrors.NetworkCode);
		harness.Store.Current.Should().Be(Valid);
		harness.Store.Clears.Should().Be(0);
	}

	/// <summary>
	/// Verifies that a refresh that fails with a server error keeps the tokens and does not sign the user out.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_RefreshAnswers500_ShouldKeepTheTokens()
	{
		using var harness = Arrange(request => IsRoute(request, RefreshRoute) ? Status(HttpStatusCode.InternalServerError) : Status(HttpStatusCode.Unauthorized));
		harness.Store.Seed(Valid);

		var result = await harness.Client.GetMeAsync();

		result.Error.Code.Should().Be(AccountErrors.ServerCode);
		harness.Store.Current.Should().Be(Valid);
	}

	/// <summary>
	/// Verifies that a network error during the refresh before a request still sends the request while the access token has not yet ended.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_ProactiveRefreshHitsNetworkError_ShouldUseTheTokenThatHasNotEnded()
	{
		using var harness = new Harness(request => IsRoute(request, RefreshRoute)
			? throw new HttpRequestException("connection lost")
			: Task.FromResult(StubHttpMessageHandler.Text(HttpStatusCode.OK, MeBody)));
		harness.Store.Seed(new SessionTokens("access-1", Start.AddSeconds(30), "refresh-1"));

		var result = await harness.Client.GetMeAsync();

		result.IsSuccess.Should().BeTrue();
		var tokens = harness.Store.Current ?? throw new InvalidOperationException("The existing tokens must be retained.");
		tokens.AccessToken.Should().Be("access-1");
		harness.Received[^1].Should().Be("GET /api/v1/me access-1");
	}

	/// <summary>
	/// Verifies that callers that find the access token expired at the same time share one refresh.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_ManyConcurrentCallsWithAnExpiredToken_ShouldRefreshOnce()
	{
		using var harness = new Harness(async request =>
		{
			await Task.Yield();
			return IsRoute(request, RefreshRoute) ? Session("access-2", "refresh-2") : StubHttpMessageHandler.Text(HttpStatusCode.OK, MeBody);
		});
		harness.Store.Seed(Expired);

		var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => harness.Client.GetMeAsync()));

		results.Should().OnlyContain(result => result.IsSuccess);
		harness.CountOf(RefreshRoute).Should().Be(1);
	}

	/// <summary>
	/// Verifies that callers that are all refused with the same access token share one refresh.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMeAsync_ManyConcurrentCallsAnswered401_ShouldRefreshOnce()
	{
		using var harness = new Harness(async request =>
		{
			await Task.Yield();
			return Route(request) switch
			{
				RefreshRoute => Session("access-2", "refresh-2"),
				MeRoute when HasBearer(request, "access-2") => StubHttpMessageHandler.Text(HttpStatusCode.OK, MeBody),
				_ => Status(HttpStatusCode.Unauthorized),
			};
		});
		harness.Store.Seed(Valid);

		var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => harness.Client.GetMeAsync()));

		results.Should().OnlyContain(result => result.IsSuccess);
		harness.CountOf(RefreshRoute).Should().Be(1);
	}
	#endregion

	#region Sign Out And Delete Tests
	/// <summary>
	/// Verifies that signing out tells the server with the bearer, deletes the tokens and clears the Google state.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_ShouldCallTheServerAndClearTheSession()
	{
		using var harness = Arrange(_ => Status(HttpStatusCode.NoContent));
		harness.Store.Seed(Valid);

		var result = await harness.Client.SignOutAsync();

		result.IsSuccess.Should().BeTrue();
		harness.Received.Should().Equal($"{SignOutRoute} access-1");
		harness.Store.Current.Should().BeNull();
		harness.Google.Clears.Should().Be(1);
	}

	/// <summary>
	/// Verifies that the phone signs out even when the server cannot be reached.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOutAsync_ServerUnreachable_ShouldStillClearTheSession()
	{
		using var harness = new Harness(_ => throw new HttpRequestException("no route"));
		harness.Store.Seed(Valid);

		var result = await harness.Client.SignOutAsync();

		result.IsSuccess.Should().BeTrue();
		harness.Store.Current.Should().BeNull();
		harness.Google.Clears.Should().Be(1);
	}

	/// <summary>
	/// Verifies that deleting the account clears the session once the server confirmed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task DeleteAccountAsync_Confirmed_ShouldClearTheSession()
	{
		using var harness = Arrange(_ => Status(HttpStatusCode.NoContent));
		harness.Store.Seed(Valid);

		var result = await harness.Client.DeleteAccountAsync();

		result.IsSuccess.Should().BeTrue();
		harness.Received.Should().Equal($"{DeleteRoute} access-1");
		harness.Store.Current.Should().BeNull();
		harness.Google.Clears.Should().Be(1);
	}

	/// <summary>
	/// Verifies that a deletion the server did not confirm keeps the session.
	/// </summary>
	/// <param name="status">The status of the server.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(HttpStatusCode.Conflict)]
	[InlineData(HttpStatusCode.InternalServerError)]
	public async Task DeleteAccountAsync_NotConfirmed_ShouldKeepTheSession(HttpStatusCode status)
	{
		using var harness = Arrange(_ => Status(status));
		harness.Store.Seed(Valid);

		var result = await harness.Client.DeleteAccountAsync();

		result.IsFailure.Should().BeTrue();
		harness.Store.Current.Should().Be(Valid);
		harness.Google.Clears.Should().Be(0);
	}

	/// <summary>
	/// Verifies that a deletion that hits a network error keeps the session and reports a network failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task DeleteAccountAsync_NetworkError_ShouldKeepTheSession()
	{
		using var harness = new Harness(_ => throw new HttpRequestException("no route"));
		harness.Store.Seed(Valid);

		var result = await harness.Client.DeleteAccountAsync();

		result.Error.Code.Should().Be(AccountErrors.NetworkCode);
		harness.Store.Current.Should().Be(Valid);
	}
	#endregion

	#region Secrecy Tests
	/// <summary>
	/// Verifies that no failure message contains a token, whatever went wrong.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Failures_ShouldNeverContainATokenInTheirMessage()
	{
		using var harness = new Harness(request => IsRoute(request, RefreshRoute)
			? throw new HttpRequestException("access-1 refresh-1 google-id-token")
			: Task.FromResult(Status(HttpStatusCode.Unauthorized)));
		harness.Store.Seed(Valid);

		var me = await harness.Client.GetMeAsync();
		var signIn = await harness.Client.SignInAsync();
		var delete = await harness.Client.DeleteAccountAsync();

		var messages = new[] { me.Error.Message, signIn.Error.Message, delete.Error.Message };
		messages.Should().OnlyContain(message => !message.Contains("access-1", StringComparison.Ordinal)
			&& !message.Contains("refresh-1", StringComparison.Ordinal)
			&& !message.Contains("google-id-token", StringComparison.Ordinal));
	}
	#endregion
}
