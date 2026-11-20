using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ParkingTracker.Contracts.Features.Sessions;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Api.E2E.Tests.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : End-to-end tests of the sign-in challenge (ADR-0014) and of the per-address limit of the session endpoints: the nonce
///              is random, single, kept in the database for five minutes, expired ones are purged on every call, the endpoint is
///              anonymous, and the eleventh session request of one address within a minute is answered 429 with Retry-After.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class SignInChallengeTests
{
	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SignInChallengeTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public SignInChallengeTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Challenge Tests
	/// <summary>
	/// Verifies that a challenge without any credential answers 200 with a nonce of 32 random bytes in base64url that expires in
	/// five minutes and is stored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Challenge_Anonymous_ShouldAnswer200WithAStoredNonce()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var response = await harness.Client.PostAsync(new Uri(SignInHarness.ChallengeRoute, UriKind.Relative), content: null);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var challenge = await response.Content.ReadFromJsonAsync<SignInChallengeResponse>();
		challenge!.Nonce.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
		challenge.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));
		await using var context = harness.OpenContext();
		var stored = await context.Set<SignInChallenge>().SingleAsync();
		stored.Nonce.Should().Be(challenge.Nonce);
		stored.ExpiresAtUtc.Should().BeCloseTo(challenge.ExpiresAt, TimeSpan.FromMilliseconds(1));
	}

	/// <summary>
	/// Verifies that two challenges give two different nonces.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Challenge_Twice_ShouldGiveDifferentNonces()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var first = await harness.NewNonceAsync();
		var second = await harness.NewNonceAsync();

		first.Should().NotBe(second);
	}

	/// <summary>
	/// Verifies that a new challenge removes the nonces that expired, and keeps the ones that did not.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Challenge_ShouldPurgeExpiredNonces()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		await using (var context = harness.OpenContext())
		{
			_ = context.Set<SignInChallenge>().Add(new SignInChallenge { Nonce = "expired-nonce", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) });
			_ = context.Set<SignInChallenge>().Add(new SignInChallenge { Nonce = "live-nonce", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(3) });
			_ = await context.SaveChangesAsync();
		}

		var fresh = await harness.NewNonceAsync();

		await using var read = harness.OpenContext();
		(await read.Set<SignInChallenge>().Select(challenge => challenge.Nonce).ToListAsync()).Should().BeEquivalentTo("live-nonce", fresh);
	}
	#endregion

	#region Rate Limit Tests
	/// <summary>
	/// Verifies that the eleventh challenge request within a minute is answered 429 with Retry-After and a problem document,
	/// after ten that were answered 200.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Challenge_EleventhWithinAMinute_ShouldAnswer429()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		for (var request = 0; request < 10; request++)
		{
			(await harness.Client.PostAsync(new Uri(SignInHarness.ChallengeRoute, UriKind.Relative), content: null)).StatusCode.Should().Be(HttpStatusCode.OK);
		}

		var refused = await harness.Client.PostAsync(new Uri(SignInHarness.ChallengeRoute, UriKind.Relative), content: null);

		refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
		refused.Headers.RetryAfter.Should().NotBeNull();
		refused.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
	}

	/// <summary>
	/// Verifies that the challenge and the sign-in share one limit per address: after ten requests of either kind, the sign-in is
	/// answered 429 too, so a client cannot get ten of each.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Exchange_AfterTenSessionRequests_ShouldAnswer429()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		for (var request = 0; request < 10; request++)
		{
			_ = await harness.NewNonceAsync();
		}

		var refused = await harness.ExchangeAsync(harness.Issuer.CreateIdToken("google-subject-1", "any-nonce"));

		refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
		refused.Headers.RetryAfter.Should().NotBeNull();
	}
	#endregion
}
