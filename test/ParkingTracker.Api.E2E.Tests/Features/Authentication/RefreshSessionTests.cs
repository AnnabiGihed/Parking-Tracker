using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using ParkingTracker.ApiService.Features.RateLimiting;
using ParkingTracker.Contracts.Features.Sessions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Api.E2E.Tests.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : End-to-end tests of the refresh (ADR-0014, ADR-0015) on the real API composition and a real PostGIS server: the
///              rotation and the access token it issues, the reuse of a rotated token that revokes the family and keeps that
///              revocation, the sliding and absolute expiry (through an adjustable clock), the refusal of an unknown or blank token,
///              the same answer for every refusal, two requests that race for one token (only one wins), and the limit of 30 per
///              minute per address, separate from the sign-in limit.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class RefreshSessionTests
{
	#region Constants
	/// <summary>
	/// The Google subject the tests sign in with.
	/// </summary>
	private const string Subject = "google-subject-refresh";

	/// <summary>
	/// The error code of the generic refusal.
	/// </summary>
	private const string RefusalCode = "Session.Refresh.Failed";
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="RefreshSessionTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public RefreshSessionTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Rotation Tests
	/// <summary>
	/// Verifies that a refresh returns a new access token and a new refresh token, spends the old one, keeps the family, and that the
	/// access token of the exchange and the one of the refresh both carry the family in the sid claim.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_ValidToken_ShouldReturnNewTokensOfTheSameFamily()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var first = await harness.SignInAsync(Subject);

		var second = await harness.RefreshSucceedingAsync(first.RefreshToken);

		second.RefreshToken.Should().MatchRegex("^[A-Za-z0-9_-]{43}$").And.NotBe(first.RefreshToken);
		second.AccessToken.Should().NotBe(first.AccessToken);
		(await harness.GetMeAsync(second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
		await using var context = harness.OpenContext();
		var account = await context.Set<UserAccount>().Include(candidate => candidate.Sessions).SingleAsync();
		account.Sessions.Should().HaveCount(2);
		var family = account.Sessions.Select(session => session.FamilyId).Distinct().Should().ContainSingle().Subject;
		account.Sessions.Single(session => session.RotatedOn is not null).TokenHash.Value.Should().Be(HashOf(first.RefreshToken));
		account.Sessions.Single(session => session.RotatedOn is null).TokenHash.Value.Should().Be(HashOf(second.RefreshToken));
		SessionClaimOf(first.AccessToken).Should().Be(family.ToString("D"));
		SessionClaimOf(second.AccessToken).Should().Be(family.ToString("D"));
		first.RefreshToken.Should().NotBe(HashOf(first.RefreshToken), "only a hash is stored");
	}

	/// <summary>
	/// Verifies that a refresh answers with the same contract as the sign-in: access token, its expiry in 15 minutes, refresh token.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_ValidToken_ShouldAnswerWithTheSessionContract()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var first = await harness.SignInAsync(Subject);

		var response = await harness.RefreshAsync(first.RefreshToken);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
		body.Select(pair => pair.Key).Should().BeEquivalentTo("accessToken", "accessTokenExpiresAt", "refreshToken");
		body["accessTokenExpiresAt"]!.GetValue<DateTimeOffset>().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
	}
	#endregion

	#region Reuse Tests
	/// <summary>
	/// Verifies that presenting a rotated token again is refused, revokes the whole family so that the new token and the old one both
	/// fail afterwards, keeps that revocation although the answer is a refusal, and leaves another family of the account alone.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_ReusedToken_ShouldRevokeTheFamilyAndKeepItButNotOtherFamilies()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		var otherDevice = await harness.SignInAsync(Subject);
		var next = await harness.RefreshSucceedingAsync(device.RefreshToken);

		var reuse = await harness.RefreshAsync(device.RefreshToken);

		await AssertRefusedAsync(harness, reuse, "refresh.reused", device.RefreshToken);
		(await harness.RefreshAsync(next.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the new token of the family is revoked as well");
		(await harness.RefreshAsync(device.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await harness.RefreshAsync(otherDevice.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "another device is not affected");
		await using var context = harness.OpenContext();
		var sessions = await context.Set<DeviceSession>().ToListAsync();
		var revokedFamilies = sessions.Where(session => session.Revoked).Select(session => session.FamilyId).Distinct().ToList();
		revokedFamilies.Should().ContainSingle("the revocation was saved although the answer was a refusal");
		sessions.Where(session => session.FamilyId == revokedFamilies[0]).Should().OnlyContain(session => session.Revoked);
		sessions.Where(session => session.FamilyId != revokedFamilies[0]).Should().OnlyContain(session => !session.Revoked);
	}
	#endregion

	#region Expiry Tests
	/// <summary>
	/// Verifies that a session unused for more than 30 days cannot be refreshed, and that one within 30 days still can.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_AfterTheSlidingExpiry_ShouldBeRefused()
	{
		var clock = new AdjustableClock();
		await using var harness = await SignInHarness.StartAsync(server, configureServices: UseClock(clock));
		var tokens = await harness.SignInAsync(Subject);
		clock.Advance(TimeSpan.FromDays(UserAccount.SlidingLifetimeDays - 1));
		var renewed = await harness.RefreshSucceedingAsync(tokens.RefreshToken);
		clock.Advance(TimeSpan.FromDays(UserAccount.SlidingLifetimeDays + 2));

		var response = await harness.RefreshAsync(renewed.RefreshToken);

		await AssertRefusedAsync(harness, response, "refresh.expired", renewed.RefreshToken);
	}

	/// <summary>
	/// Verifies that a family cannot be refreshed after 90 days from the sign-in, however regularly it refreshed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_AfterTheAbsoluteExpiry_ShouldBeRefusedDespiteRegularUse()
	{
		var clock = new AdjustableClock();
		await using var harness = await SignInHarness.StartAsync(server, configureServices: UseClock(clock));
		var tokens = await harness.SignInAsync(Subject);
		for (var step = 0; step < 3; step++)
		{
			clock.Advance(TimeSpan.FromDays(29));
			tokens = await harness.RefreshSucceedingAsync(tokens.RefreshToken);
		}

		clock.Advance(TimeSpan.FromDays(4));
		var response = await harness.RefreshAsync(tokens.RefreshToken);

		await AssertRefusedAsync(harness, response, "refresh.expired", tokens.RefreshToken);
	}
	#endregion

	#region Refusal Tests
	/// <summary>
	/// Verifies that a token no session holds is refused, with a reason code in the log and without the token anywhere in the answer
	/// or the log.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_UnknownToken_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		const string Unknown = "a-token-that-no-session-holds";

		var response = await harness.RefreshAsync(Unknown);

		await AssertRefusedAsync(harness, response, "refresh.unknown", Unknown);
	}

	/// <summary>
	/// Verifies that an empty or blank token is refused as malformed, with the same answer.
	/// </summary>
	/// <param name="token">The token text.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task Refresh_BlankToken_ShouldBeRefused(string token)
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var response = await harness.RefreshAsync(token);

		await AssertRefusedAsync(harness, response, "refresh.malformed", nonSecret: null);
	}

	/// <summary>
	/// Verifies that a token longer than any refresh token is refused unread.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_OversizedToken_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var response = await harness.RefreshAsync(new string('a', 500));

		await AssertRefusedAsync(harness, response, "refresh.malformed", nonSecret: null);
	}

	/// <summary>
	/// Verifies that a body above the limit of the endpoint is answered 413. It runs on a real Kestrel server, because the in-memory
	/// server does not enforce the limit.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_OversizedBody_ShouldAnswer413()
	{
		await using var harness = await SignInHarness.StartAsync(server, useKestrel: true);
		using var body = new StringContent("{\"refreshToken\":\"" + new string('a', 5000) + "\"}", Encoding.UTF8, "application/json");

		var response = await harness.Client.PostAsync(new Uri(SignInHarness.RefreshRoute, UriKind.Relative), body);

		response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
	}

	/// <summary>
	/// Verifies that a body without the token property is a plain 400.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_BodyWithoutToken_ShouldAnswer400()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		using var body = new StringContent("{}", Encoding.UTF8, "application/json");

		var response = await harness.Client.PostAsync(new Uri(SignInHarness.RefreshRoute, UriKind.Relative), body);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	/// <summary>
	/// Verifies that every cause of refusal gives the very same answer body, so a caller cannot tell the checks apart.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_EveryRefusal_ShouldGiveTheSameAnswer()
	{
		var clock = new AdjustableClock();
		await using var harness = await SignInHarness.StartAsync(server, configureServices: UseClock(clock));
		var reused = await harness.SignInAsync(Subject);
		_ = await harness.RefreshSucceedingAsync(reused.RefreshToken);
		var revoked = await harness.SignInAsync(Subject);
		(await harness.SignOutAsync(revoked.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		var expired = await harness.SignInAsync(Subject);
		var attempts = new[] { "unknown-token", " ", reused.RefreshToken, revoked.RefreshToken };

		var bodies = new List<string>();
		foreach (var attempt in attempts)
		{
			var response = await harness.RefreshAsync(attempt);
			response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
			bodies.Add(await response.Content.ReadAsStringAsync());
		}

		clock.Advance(TimeSpan.FromDays(40));
		var late = await harness.RefreshAsync(expired.RefreshToken);
		late.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		bodies.Add(await late.Content.ReadAsStringAsync());
		bodies.Distinct(StringComparer.Ordinal).Should().ContainSingle("the answer must not reveal which check failed");
	}
	#endregion

	#region Concurrency Tests
	/// <summary>
	/// Verifies that two requests that present the same token at the same moment, both having loaded the account before either
	/// saves, end with exactly one success: the loser fails on the concurrency tokens and is refused, and the family has exactly
	/// one live token.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_TwoRequestsForTheSameToken_ShouldGiveExactlyOneSuccessor()
	{
		using var rendezvous = new Barrier(2);
		await using var harness = await SignInHarness.StartAsync(server, configureServices: services => RendezvousRepository.Install(services, rendezvous, RendezvousRepository.HeldRead.ByRefreshToken));
		var tokens = await harness.SignInAsync(Subject);

		var responses = await Task.WhenAll(harness.RefreshAsync(tokens.RefreshToken), harness.RefreshAsync(tokens.RefreshToken));

		responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
		responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1);
		await using var context = harness.OpenContext();
		var sessions = await context.Set<DeviceSession>().ToListAsync();
		sessions.Should().HaveCount(2, "the loser's new session was rolled back with its save");
		sessions.Count(session => session.RotatedOn is null && !session.Revoked).Should().Be(1, "never two valid successors");
		harness.Factory.Log.Lines.Should().Contain(line => line.Contains("Refresh refused: save.failed", StringComparison.Ordinal));
	}
	#endregion

	#region Rate Limit Tests
	/// <summary>
	/// Verifies that the 31st refresh request of one address within a minute answers 429 with Retry-After, that refusals count
	/// against the limit, and that the sign-in limit of the same address is a separate one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Refresh_BeyondThirtyPerMinute_ShouldAnswer429WithoutUsingTheSignInLimit()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		for (var request = 0; request < RateLimitSettings.RefreshPermitsPerWindow; request++)
		{
			(await harness.RefreshAsync("unknown-token")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		}

		var refused = await harness.RefreshAsync("unknown-token");
		var challenge = await harness.Client.PostAsync(new Uri(SignInHarness.ChallengeRoute, UriKind.Relative), content: null);

		refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
		refused.Headers.RetryAfter.Should().NotBeNull();
		refused.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
		challenge.StatusCode.Should().Be(HttpStatusCode.OK, "the refresh limit does not use up the sign-in limit");
		RateLimitSettings.RefreshPermitsPerWindow.Should().Be(30);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the container change that replaces the system clock.
	/// </summary>
	/// <param name="clock">The clock the API should use.</param>
	/// <returns>The change.</returns>
	private static Action<IServiceCollection> UseClock(AdjustableClock clock)
	{
		return services =>
		{
			services.RemoveAll<TimeProvider>();
			_ = services.AddSingleton<TimeProvider>(clock);
		};
	}

	/// <summary>
	/// Reads the session claim of an access token.
	/// </summary>
	/// <param name="accessToken">The access token.</param>
	/// <returns>The value of the sid claim.</returns>
	private static string SessionClaimOf(string accessToken)
	{
		return new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetClaim("sid").Value;
	}

	/// <summary>
	/// Computes the stored hash of a refresh token.
	/// </summary>
	/// <param name="token">The token.</param>
	/// <returns>SHA-256 in lowercase hexadecimal.</returns>
	private static string HashOf(string token)
	{
		return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
	}

	/// <summary>
	/// Checks that an answer is the generic refusal and that the log has a reason code and no secret.
	/// </summary>
	/// <param name="harness">The harness the call was made on.</param>
	/// <param name="response">The answer.</param>
	/// <param name="reason">The reason code the log must carry.</param>
	/// <param name="nonSecret">The token that must not be in the answer or the log; <see langword="null"/> for none.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	private static async Task AssertRefusedAsync(SignInHarness harness, HttpResponseMessage response, string reason, string? nonSecret)
	{
		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
		var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
		JsonNode.Parse(body)!["type"]!.GetValue<string>().Should().Be(RefusalCode);
		var log = string.Join('\n', harness.Factory.Log.Lines);
		log.Should().Contain($"Refresh refused: {reason}");
		log.Should().NotContain(Subject).And.NotContain("eyJ");
		if (nonSecret is not null)
		{
			body.Should().NotContain(nonSecret);
			log.Should().NotContain(nonSecret);
		}
	}
	#endregion
}
