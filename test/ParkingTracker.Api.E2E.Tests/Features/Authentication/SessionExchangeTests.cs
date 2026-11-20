using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParkingTracker.Contracts.Features.Sessions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;
using ParkingTracker.Testing.PostgreSql;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Api.E2E.Tests.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : End-to-end tests of the sign-in (ADR-0014, ADR-0025) on the real API composition, a real PostGIS server and a test
///              issuer in place of Google. They prove the valid exchange, the reuse of an account, and every refusal: wrong audience,
///              wrong issuer, expired token, no signature, a wrong signature, another algorithm, no nonce, an unknown, a replayed
///              and an expired nonce. Every refusal must be the same 401 answer, create nothing and leak neither the token nor the
///              nonce; the cause only reaches the log, as a reason code.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class SessionExchangeTests
{
	#region Constants
	/// <summary>
	/// The Google subject the tests sign in with.
	/// </summary>
	private const string Subject = "google-subject-1";

	/// <summary>
	/// The error code of the generic refusal.
	/// </summary>
	private const string RefusalCode = "Session.SignIn.Failed";
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SessionExchangeTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public SessionExchangeTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Valid Exchange Tests
	/// <summary>
	/// Verifies that a valid exchange creates one account and one session, stores only the hash of the subject and of the refresh
	/// token, and returns an access token and a refresh token.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_ValidToken_ShouldCreateOneAccountAndOneSession()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var tokens = await harness.SignInAsync(Subject);

		tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
		tokens.RefreshToken.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "32 random bytes in base64url");
		tokens.AccessTokenExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
		await using var context = harness.OpenContext();
		var account = await context.Set<UserAccount>().Include(candidate => candidate.Sessions).SingleAsync();
		account.SubjectHash.Value.Should().Be(HashSubject(harness.Factory.Secrets.SubjectHashKey, Subject)).And.NotContain(Subject);
		account.Sessions.Should().ContainSingle().Which.TokenHash.Value.Should().Be(Sha256Hex(tokens.RefreshToken)).And.NotBe(tokens.RefreshToken);
		(await context.Set<SignInChallenge>().CountAsync()).Should().Be(0, "the nonce was consumed");
	}

	/// <summary>
	/// Verifies that the same Google subject signing in twice gets the same account and two sessions.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_SameSubjectTwice_ShouldUseTheSameAccountAndStartTwoSessions()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var first = await harness.SignInAsync(Subject);
		var second = await harness.SignInAsync(Subject);

		await using var context = harness.OpenContext();
		var account = await context.Set<UserAccount>().Include(candidate => candidate.Sessions).SingleAsync();
		account.Sessions.Should().HaveCount(2);
		account.Sessions.Select(session => session.FamilyId).Distinct().Should().HaveCount(2, "every sign-in is a device of its own");
		first.RefreshToken.Should().NotBe(second.RefreshToken);
	}

	/// <summary>
	/// Verifies the race of first sign-ins: two requests for one new subject at the same moment both succeed, on one account with
	/// one session each; the one that loses the unique index starts its session on the account of the winner.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_TwoConcurrentFirstSignInsOfOneSubject_ShouldBothSucceedOnOneAccount()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var tokens = new List<string>();
		for (var attempt = 0; attempt < 2; attempt++)
		{
			tokens.Add(harness.Issuer.CreateIdToken(Subject, await harness.NewNonceAsync()));
		}

		var responses = await Task.WhenAll(tokens.Select(harness.ExchangeAsync));

		responses.Select(response => response.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
		using var context = harness.OpenContext();
		var account = await context.Set<UserAccount>().Include(candidate => candidate.Sessions).SingleAsync();
		account.Sessions.Should().HaveCount(2);
	}

	/// <summary>
	/// Verifies that two different Google subjects get two accounts.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_TwoSubjects_ShouldCreateTwoAccounts()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		_ = await harness.SignInAsync("google-subject-a");
		_ = await harness.SignInAsync("google-subject-b");

		await using var context = harness.OpenContext();
		(await context.Set<UserAccount>().CountAsync()).Should().Be(2);
	}
	#endregion

	#region Refusal Tests
	/// <summary>
	/// Verifies that a token for another audience is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_WrongAudience_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var nonce = await harness.NewNonceAsync();

		var response = await harness.ExchangeAsync(harness.Issuer.CreateIdToken(Subject, nonce, audience: "another-client.apps.googleusercontent.com"));

		await AssertRefusedAsync(harness, response, "token.audience", nonce);
	}

	/// <summary>
	/// Verifies that a token from another issuer is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_WrongIssuer_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var nonce = await harness.NewNonceAsync();

		var response = await harness.ExchangeAsync(harness.Issuer.CreateIdToken(Subject, nonce, issuer: "https://evil.example"));

		await AssertRefusedAsync(harness, response, "token.issuer", nonce);
	}

	/// <summary>
	/// Verifies that an expired token is refused, whatever the nonce.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_ExpiredToken_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var nonce = await harness.NewNonceAsync();

		var response = await harness.ExchangeAsync(harness.Issuer.CreateIdToken(Subject, nonce, expires: DateTimeOffset.UtcNow.AddMinutes(-5)));

		await AssertRefusedAsync(harness, response, "token.lifetime", nonce);
	}

	/// <summary>
	/// Verifies that a token without signature (algorithm none) is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_UnsignedToken_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var nonce = await harness.NewNonceAsync();

		var response = await harness.ExchangeAsync(TestGoogleIssuer.CreateUnsignedIdToken(Subject, nonce));

		await AssertRefusedAsync(harness, response, "token.", nonce);
	}

	/// <summary>
	/// Verifies that a token signed with a key Google does not publish is refused, and that the failed attempt does not burn the
	/// nonce: the nonce is consumed only after the signature is proven.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_BadSignature_ShouldBeRefusedAndLeaveTheNonceUnused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var nonce = await harness.NewNonceAsync();

		var forged = await harness.ExchangeAsync(harness.Issuer.CreateIdToken(Subject, nonce, signedByStranger: true));
		await AssertRefusedAsync(harness, forged, "token.signature", nonce, expectNonceConsumed: false);
		var genuine = await harness.ExchangeAsync(harness.Issuer.CreateIdToken(Subject, nonce));

		genuine.StatusCode.Should().Be(HttpStatusCode.OK, "a forged attempt must not burn the nonce of the real user");
	}

	/// <summary>
	/// Verifies that a token signed with another algorithm than RS256 is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_TokenSignedWithAnotherAlgorithm_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var nonce = await harness.NewNonceAsync();

		var response = await harness.ExchangeAsync(harness.Issuer.CreateSymmetricIdToken(Subject, nonce));

		await AssertRefusedAsync(harness, response, "token.", nonce);
	}

	/// <summary>
	/// Verifies that a token without a nonce is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_TokenWithoutNonce_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var response = await harness.ExchangeAsync(harness.Issuer.CreateIdToken(Subject, nonce: null));

		await AssertRefusedAsync(harness, response, "token.claims", nonce: null);
	}

	/// <summary>
	/// Verifies that a token whose nonce the API never issued is refused.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_UnknownNonce_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		const string Unknown = "a-nonce-the-api-never-issued";

		var response = await harness.ExchangeAsync(harness.Issuer.CreateIdToken(Subject, Unknown));

		await AssertRefusedAsync(harness, response, "nonce.rejected", Unknown);
	}

	/// <summary>
	/// Verifies that the second use of a nonce is refused: after a successful sign-in the same token cannot sign in again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_ReplayedNonce_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var nonce = await harness.NewNonceAsync();
		var token = harness.Issuer.CreateIdToken(Subject, nonce);
		(await harness.ExchangeAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK);

		var replay = await harness.ExchangeAsync(token);

		await AssertRefusedAsync(harness, replay, "nonce.rejected", nonce, expectedAccounts: 1);
		await using var context = harness.OpenContext();
		(await context.Set<UserAccount>().Include(account => account.Sessions).SingleAsync()).Sessions.Should().ContainSingle("the replay started no session");
	}

	/// <summary>
	/// Verifies that a nonce past its five minutes is refused, even with a valid token.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_ExpiredNonce_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		const string Expired = "an-expired-nonce";
		await using (var context = harness.OpenContext())
		{
			_ = context.Set<SignInChallenge>().Add(new SignInChallenge { Nonce = Expired, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) });
			_ = await context.SaveChangesAsync();
		}

		var response = await harness.ExchangeAsync(harness.Issuer.CreateIdToken(Subject, Expired));

		await AssertRefusedAsync(harness, response, "nonce.rejected", Expired);
	}

	/// <summary>
	/// Verifies that an empty token and a token far too long are refused unread.
	/// </summary>
	/// <param name="token">The token text.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(" ")]
	[InlineData("not-a-jwt")]
	public async Task Exchange_BlankOrMalformedToken_ShouldBeRefused(string token)
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var response = await harness.ExchangeAsync(token);

		await AssertRefusedAsync(harness, response, "token.", nonce: null);
	}

	/// <summary>
	/// Verifies that a token longer than any real ID token is refused without being parsed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_OversizedToken_ShouldBeRefused()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var response = await harness.ExchangeAsync(new string('a', 9000));

		await AssertRefusedAsync(harness, response, "token.missing", nonce: null);
	}

	/// <summary>
	/// Verifies that a body above the limit of the endpoint is answered 413, whatever it holds, and creates nothing. It runs on a real
	/// Kestrel server, because the in-memory server does not enforce the limit.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_OversizedBody_ShouldAnswer413()
	{
		await using var harness = await SignInHarness.StartAsync(server, useKestrel: true);
		using var body = new StringContent("{\"idToken\":\"" + new string('a', 20000) + "\"}", Encoding.UTF8, "application/json");

		var response = await harness.Client.PostAsync(new Uri(SignInHarness.SessionRoute, UriKind.Relative), body);

		response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
		using var context = harness.OpenContext();
		(await context.Set<UserAccount>().CountAsync()).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a body without the token property is a plain 400 and creates nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_BodyWithoutToken_ShouldAnswer400()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		using var body = new StringContent("{}", Encoding.UTF8, "application/json");

		var response = await harness.Client.PostAsync(new Uri(SignInHarness.SessionRoute, UriKind.Relative), body);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		await using var context = harness.OpenContext();
		(await context.Set<UserAccount>().CountAsync()).Should().Be(0);
	}

	/// <summary>
	/// Verifies that every cause of refusal gives the very same answer body, so a caller cannot tell the checks apart.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_EveryRefusal_ShouldGiveTheSameAnswer()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var nonce = await harness.NewNonceAsync();
		var attempts = new[]
		{
			harness.Issuer.CreateIdToken(Subject, nonce, audience: "other"),
			harness.Issuer.CreateIdToken(Subject, nonce, issuer: "https://evil.example"),
			harness.Issuer.CreateIdToken(Subject, nonce, expires: DateTimeOffset.UtcNow.AddMinutes(-5)),
			harness.Issuer.CreateIdToken(Subject, nonce, signedByStranger: true),
			TestGoogleIssuer.CreateUnsignedIdToken(Subject, nonce),
			harness.Issuer.CreateIdToken(Subject, "unknown-nonce"),
			"garbage",
		};

		var bodies = new List<string>();
		foreach (var attempt in attempts)
		{
			var response = await harness.ExchangeAsync(attempt);
			response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
			bodies.Add(await response.Content.ReadAsStringAsync());
		}

		bodies.Distinct(StringComparer.Ordinal).Should().ContainSingle("the answer must not reveal which check failed");
	}
	#endregion

	#region Consumed Nonce Tests
	/// <summary>
	/// Verifies that a nonce stays consumed when the exchange fails after the signature check, here because the save fails: the
	/// consumption is not part of the unit of work, and the answer is the same generic refusal.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_FailureAfterTheSignatureCheck_ShouldKeepTheNonceConsumed()
	{
		await using var harness = await SignInHarness.StartAsync(server, configureServices: services =>
		{
			services.RemoveAll<IUnitOfWork>();
			_ = services.AddScoped<IUnitOfWork, FailingUnitOfWork>();
		});
		var nonce = await harness.NewNonceAsync();
		var token = harness.Issuer.CreateIdToken(Subject, nonce);

		var first = await harness.ExchangeAsync(token);

		await AssertRefusedAsync(harness, first, "save.failed", nonce);
		await using var context = harness.OpenContext();
		(await context.Set<SignInChallenge>().CountAsync()).Should().Be(0, "the nonce is gone although the sign-in failed");
		(await harness.ExchangeAsync(token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Checks that an answer is the generic refusal and that the refusal left no trace: no account, a reason code in the log
	/// and no secret in the answer or the log.
	/// </summary>
	/// <param name="harness">The harness the call was made on.</param>
	/// <param name="response">The answer.</param>
	/// <param name="reasonPrefix">The start of the reason code the log must carry.</param>
	/// <param name="nonce">The nonce of the attempt; must not appear in the answer or the log.</param>
	/// <param name="expectNonceConsumed">Whether the nonce must be gone from the database; checked only when a nonce is given.</param>
	/// <param name="expectedAccounts">The accounts that exist after the attempt: none, unless an earlier sign-in of the test made one.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	private static async Task AssertRefusedAsync(SignInHarness harness, HttpResponseMessage response, string reasonPrefix, string? nonce, bool expectNonceConsumed = true, int expectedAccounts = 0)
	{
		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
		var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
		JsonNode.Parse(body)!["type"]!.GetValue<string>().Should().Be(RefusalCode);
		var log = string.Join('\n', harness.Factory.Log.Lines);
		log.Should().Contain($"Sign-in refused: {reasonPrefix}");
		if (nonce is not null)
		{
			body.Should().NotContain(nonce);
			log.Should().NotContain(nonce);
		}

		log.Should().NotContain(Subject, "the subject is never logged");
		log.Should().NotContain("eyJ", "no token is ever logged");
		using var context = harness.OpenContext();
		(await context.Set<UserAccount>().CountAsync().ConfigureAwait(false)).Should().Be(expectedAccounts, "a refusal creates no account");
		if (nonce is not null && !expectNonceConsumed)
		{
			(await context.Set<SignInChallenge>().CountAsync(challenge => challenge.Nonce == nonce).ConfigureAwait(false)).Should().Be(1);
		}
	}

	/// <summary>
	/// Computes the keyed hash of a subject as the API must: HMAC-SHA256 in lowercase hexadecimal.
	/// </summary>
	/// <param name="base64Key">The subject hash key as base64.</param>
	/// <param name="subject">The subject.</param>
	/// <returns>The hash.</returns>
	private static string HashSubject(string base64Key, string subject)
	{
		return Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromBase64String(base64Key), Encoding.UTF8.GetBytes(subject)));
	}

	/// <summary>
	/// Computes the SHA-256 hash of a text in lowercase hexadecimal.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <returns>The hash.</returns>
	private static string Sha256Hex(string text)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : A unit of work whose save always fails, to prove that the consumption of a nonce does not depend on it.
	/// </summary>
	private sealed class FailingUnitOfWork : IUnitOfWork
	{
		#region IUnitOfWork Implementation
		/// <inheritdoc />
		public Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(Result.Failure(new Error("Test.Save.Failed", "The save failed on purpose."), ResultExceptionType.None));
		}
		#endregion
	}
	#endregion
}
