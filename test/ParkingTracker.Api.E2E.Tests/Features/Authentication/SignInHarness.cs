using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Contracts.Features.Sessions;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Api.E2E.Tests.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Everything one sign-in test needs: a migrated database on the real PostGIS server, the API in Production on the
///              role that serves it, a test issuer in place of Google, and the calls of the sign-in as a phone makes them. It owns
///              what it starts and disposes it all.
/// </summary>
public sealed class SignInHarness : IAsyncDisposable
{
	#region Constants
	/// <summary>
	/// The route of the challenge endpoint.
	/// </summary>
	public const string ChallengeRoute = "/api/v1/sessions/challenges";

	/// <summary>
	/// The route of the sign-in endpoint.
	/// </summary>
	public const string SessionRoute = "/api/v1/sessions";

	/// <summary>
	/// The route of the own-account endpoint.
	/// </summary>
	public const string MeRoute = "/api/v1/me";

	/// <summary>
	/// The route of the refresh endpoint.
	/// </summary>
	public const string RefreshRoute = "/api/v1/sessions/refresh";

	/// <summary>
	/// The route of the sign-out endpoint.
	/// </summary>
	public const string CurrentSessionRoute = "/api/v1/sessions/current";
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SignInHarness"/> class.
	/// </summary>
	/// <param name="database">The migrated database.</param>
	/// <param name="issuer">The test issuer.</param>
	/// <param name="factory">The API under test.</param>
	private SignInHarness(TestDatabase database, TestGoogleIssuer issuer, ApiFactory factory)
	{
		Database = database;
		Issuer = issuer;
		Factory = factory;
		Client = factory.CreateClient();
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the migrated database.
	/// </summary>
	public TestDatabase Database { get; }

	/// <summary>
	/// Gets the test issuer.
	/// </summary>
	public TestGoogleIssuer Issuer { get; }

	/// <summary>
	/// Gets the API under test.
	/// </summary>
	public ApiFactory Factory { get; }

	/// <summary>
	/// Gets the client of the API.
	/// </summary>
	public HttpClient Client { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Migrates a database and starts the API in Production on it.
	/// </summary>
	/// <param name="server">The shared server.</param>
	/// <param name="secrets">The secrets of the host; generated when omitted.</param>
	/// <param name="settings">Configuration values that override the defaults of the factory.</param>
	/// <param name="configureServices">Changes to the container after the host registered its own.</param>
	/// <param name="useKestrel">Whether to serve through a real Kestrel server instead of the in-memory one.</param>
	/// <returns>The started harness.</returns>
	public static async Task<SignInHarness> StartAsync(
		PostgisServer server,
		ApiSecrets? secrets = null,
		IReadOnlyDictionary<string, string?>? settings = null,
		Action<IServiceCollection>? configureServices = null,
		bool useKestrel = false)
	{
		ArgumentNullException.ThrowIfNull(server);
		var database = await server.CreateDatabaseWithRolesAsync().ConfigureAwait(false);
		using (var owner = CreateContext(database.OwnerConnectionString))
		{
			await owner.Database.MigrateAsync().ConfigureAwait(false);
		}

		var issuer = new TestGoogleIssuer();
		var factory = new ApiFactory("Production", database.ApiConnectionString, secrets: secrets, googleIssuer: issuer, settings: settings, configureServices: configureServices, useKestrel: useKestrel);
		return new SignInHarness(database, issuer, factory);
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Opens a context on the database as the owner role, to read and prepare what the tests check.
	/// </summary>
	/// <returns>The context; the caller disposes it.</returns>
	public ParkingDataDbContext OpenContext()
	{
		return CreateContext(Database.OwnerConnectionString);
	}

	/// <summary>
	/// Asks for a nonce, as the phone does before it calls Google.
	/// </summary>
	/// <returns>The nonce.</returns>
	public async Task<string> NewNonceAsync()
	{
		var response = await Client.PostAsync(new Uri(ChallengeRoute, UriKind.Relative), content: null).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		var challenge = await response.Content.ReadFromJsonAsync<SignInChallengeResponse>().ConfigureAwait(false);
		return challenge!.Nonce;
	}

	/// <summary>
	/// Sends an ID token to the sign-in endpoint.
	/// </summary>
	/// <param name="idToken">The ID token.</param>
	/// <returns>The answer.</returns>
	public Task<HttpResponseMessage> ExchangeAsync(string idToken)
	{
		return Client.PostAsJsonAsync(new Uri(SessionRoute, UriKind.Relative), new CreateSessionRequest(idToken));
	}

	/// <summary>
	/// Signs a subject in with a fresh nonce and a good ID token, and expects success.
	/// </summary>
	/// <param name="subject">The Google subject.</param>
	/// <returns>The tokens.</returns>
	public async Task<SessionResponse> SignInAsync(string subject)
	{
		var nonce = await NewNonceAsync().ConfigureAwait(false);
		var response = await ExchangeAsync(Issuer.CreateIdToken(subject, nonce)).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<SessionResponse>().ConfigureAwait(false))!;
	}

	/// <summary>
	/// Sends a refresh token to the refresh endpoint.
	/// </summary>
	/// <param name="refreshToken">The refresh token.</param>
	/// <returns>The answer.</returns>
	public Task<HttpResponseMessage> RefreshAsync(string refreshToken)
	{
		return Client.PostAsJsonAsync(new Uri(RefreshRoute, UriKind.Relative), new RefreshSessionRequest(refreshToken));
	}

	/// <summary>
	/// Refreshes a session and expects success.
	/// </summary>
	/// <param name="refreshToken">The refresh token.</param>
	/// <returns>The new tokens.</returns>
	public async Task<SessionResponse> RefreshSucceedingAsync(string refreshToken)
	{
		var response = await RefreshAsync(refreshToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<SessionResponse>().ConfigureAwait(false))!;
	}

	/// <summary>
	/// Signs out the device of an access token.
	/// </summary>
	/// <param name="accessToken">The access token; <see langword="null"/> for none.</param>
	/// <returns>The answer.</returns>
	public Task<HttpResponseMessage> SignOutAsync(string? accessToken)
	{
		return SendAuthenticatedAsync(HttpMethod.Delete, CurrentSessionRoute, accessToken);
	}

	/// <summary>
	/// Erases the account of an access token.
	/// </summary>
	/// <param name="accessToken">The access token; <see langword="null"/> for none.</param>
	/// <returns>The answer.</returns>
	public Task<HttpResponseMessage> DeleteAccountAsync(string? accessToken)
	{
		return SendAuthenticatedAsync(HttpMethod.Delete, MeRoute, accessToken);
	}

	/// <summary>
	/// Calls the own-account endpoint, with a bearer token when one is given.
	/// </summary>
	/// <param name="accessToken">The access token; <see langword="null"/> for none.</param>
	/// <returns>The answer.</returns>
	public Task<HttpResponseMessage> GetMeAsync(string? accessToken)
	{
		return SendAuthenticatedAsync(HttpMethod.Get, MeRoute, accessToken);
	}
	#endregion

	#region IAsyncDisposable Implementation
	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		Client.Dispose();
		await Factory.DisposeAsync().ConfigureAwait(false);
		Issuer.Dispose();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Sends a request with a bearer token when one is given.
	/// </summary>
	/// <param name="method">The HTTP method.</param>
	/// <param name="route">The route.</param>
	/// <param name="accessToken">The access token; <see langword="null"/> for none.</param>
	/// <returns>The answer.</returns>
	private async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpMethod method, string route, string? accessToken)
	{
		using var request = new HttpRequestMessage(method, route);
		if (accessToken is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		}

		return await Client.SendAsync(request).ConfigureAwait(false);
	}

	/// <summary>
	/// Creates a context on a connection string.
	/// </summary>
	/// <param name="connectionString">The connection string.</param>
	/// <returns>The context.</returns>
	private static ParkingDataDbContext CreateContext(string connectionString)
	{
		var options = new DbContextOptionsBuilder<ParkingDataDbContext>()
			.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
			.Options;
		return new ParkingDataDbContext(options);
	}
	#endregion
}
