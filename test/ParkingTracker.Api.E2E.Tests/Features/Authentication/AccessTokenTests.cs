using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ParkingTracker.Contracts.Features.Accounts;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Api.E2E.Tests.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : End-to-end tests of the access tokens (ADR-0014): the claims, header and signature of the token the exchange
///              issues, and the bearer authentication of the API on <c>GET /api/v1/me</c>: 401 without a token, with a token of
///              another key, issuer or audience, an expired, unsigned or symmetric one, or one of an unknown account; 200 with a
///              valid one, with the previous key during a rotation, and within the clock skew.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class AccessTokenTests
{
	#region Constants
	/// <summary>
	/// The Google subject the tests sign in with.
	/// </summary>
	private const string Subject = "google-subject-1";
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccessTokenTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public AccessTokenTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Issued Token Tests
	/// <summary>
	/// Verifies the claims, the header and the signature of an issued access token: ES256, the key id is the JWK thumbprint of
	/// the current key, the configured issuer, the audience of the API, the account identifier as subject, 15 minutes of life.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task IssuedToken_ShouldCarryTheExpectedClaimsAndVerifyWithTheCurrentKey()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var tokens = await harness.SignInAsync(Subject);
		await using var context = harness.OpenContext();
		var account = await context.Set<UserAccount>().SingleAsync();
		using var currentKey = AccessTokenForger.ReadKey(harness.Factory.Secrets.CurrentKeyPem);

		var token = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken);

		token.Alg.Should().Be("ES256");
		token.Kid.Should().Be(AccessTokenForger.Thumbprint(currentKey));
		token.Issuer.Should().Be(ApiFactory.TokenIssuer);
		token.Audiences.Should().ContainSingle().Which.Should().Be("parkingtracker-api");
		token.Subject.Should().Be(account.Id.Value.ToString("D"));
		(token.ValidTo - token.ValidFrom).Should().Be(TimeSpan.FromMinutes(15));
		token.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
		new DateTimeOffset(token.ValidTo).Should().Be(tokens.AccessTokenExpiresAt);
		var verification = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.AccessToken, new TokenValidationParameters
		{
			ValidIssuer = ApiFactory.TokenIssuer,
			ValidAudience = AccessTokenForger.Audience,
			IssuerSigningKey = new ECDsaSecurityKey(currentKey) { KeyId = AccessTokenForger.Thumbprint(currentKey) },
			ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
		});
		verification.IsValid.Should().BeTrue("the signature is made by the current key");
	}
	#endregion

	#region Me Tests
	/// <summary>
	/// Verifies that the own account is answered to the holder of a valid access token.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMe_ValidToken_ShouldAnswer200WithTheAccount()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		_ = await harness.SignInAsync(Subject);
		var tokens = await harness.SignInAsync(Subject);

		var response = await harness.GetMeAsync(tokens.AccessToken);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var me = await response.Content.ReadFromJsonAsync<MeResponse>();
		await using var context = harness.OpenContext();
		var account = await context.Set<UserAccount>().SingleAsync();
		me!.AccountId.Should().Be(account.Id.Value);
		me.CreatedOn.Should().Be(account.CreatedOn);
		me.LastSeenOn.Should().Be(account.LastSeenOn);
		me.ActiveSessionCount.Should().Be(2);
	}

	/// <summary>
	/// Verifies that a request without a token, or with a header that is not a bearer token, is answered 401.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMe_WithoutToken_ShouldAnswer401()
	{
		await using var harness = await SignInHarness.StartAsync(server);

		var response = await harness.GetMeAsync(accessToken: null);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		response.Headers.WwwAuthenticate.Should().ContainSingle().Which.Scheme.Should().Be("Bearer");
	}

	/// <summary>
	/// Verifies that a token signed by a key the API does not hold is refused, whether it claims the key id of the real key or
	/// an unknown one.
	/// </summary>
	/// <param name="claimTheRealKeyId">Whether the forged token carries the key id of the current key.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task GetMe_TokenSignedByAnotherKey_ShouldAnswer401(bool claimTheRealKeyId)
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var tokens = await harness.SignInAsync(Subject);
		var subject = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken).Subject;
		using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		using var current = AccessTokenForger.ReadKey(harness.Factory.Secrets.CurrentKeyPem);
		var forged = AccessTokenForger.Create(stranger, subject, keyId: claimTheRealKeyId ? AccessTokenForger.Thumbprint(current) : null);

		var response = await harness.GetMeAsync(forged);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// Verifies that a token with a wrong issuer or a wrong audience is refused although its signature is right.
	/// </summary>
	/// <param name="issuer">The issuer of the forged token.</param>
	/// <param name="audience">The audience of the forged token.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("https://evil.example", "parkingtracker-api")]
	[InlineData(ApiFactory.TokenIssuer, "another-api")]
	public async Task GetMe_WrongIssuerOrAudience_ShouldAnswer401(string issuer, string audience)
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var tokens = await harness.SignInAsync(Subject);
		var subject = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken).Subject;
		using var current = AccessTokenForger.ReadKey(harness.Factory.Secrets.CurrentKeyPem);

		var response = await harness.GetMeAsync(AccessTokenForger.Create(current, subject, issuer, audience));

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// Verifies that an unsigned token, a token signed with a shared secret and a text that is no token are refused.
	/// </summary>
	/// <param name="kind">The kind of forgery.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("none")]
	[InlineData("hmac")]
	[InlineData("garbage")]
	public async Task GetMe_UnsignedSymmetricOrGarbageToken_ShouldAnswer401(string kind)
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var tokens = await harness.SignInAsync(Subject);
		var subject = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken).Subject;
		var forged = kind switch
		{
			"none" => AccessTokenForger.CreateUnsigned(subject),
			"hmac" => AccessTokenForger.CreateSymmetric(subject),
			_ => "not.a.token",
		};

		var response = await harness.GetMeAsync(forged);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// Verifies that an expired token is refused beyond the 30 seconds of skew and accepted within them.
	/// </summary>
	/// <param name="secondsAgo">How long ago the token expired.</param>
	/// <param name="expected">The expected status.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(10, HttpStatusCode.OK)]
	[InlineData(120, HttpStatusCode.Unauthorized)]
	public async Task GetMe_ExpiredToken_ShouldHonourOnlyTheThirtySecondSkew(int secondsAgo, HttpStatusCode expected)
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var tokens = await harness.SignInAsync(Subject);
		var subject = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken).Subject;
		using var current = AccessTokenForger.ReadKey(harness.Factory.Secrets.CurrentKeyPem);

		var response = await harness.GetMeAsync(AccessTokenForger.Create(current, subject, expires: DateTimeOffset.UtcNow.AddSeconds(-secondsAgo)));

		response.StatusCode.Should().Be(expected);
	}

	/// <summary>
	/// Verifies that a correctly signed token of an account that does not exist, or whose subject is no identifier, is refused.
	/// </summary>
	/// <param name="subject">The subject of the forged token.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("3f2c1e5a-7a52-4b8e-9d0e-0a5b1f6d9c11")]
	[InlineData("not-a-guid")]
	public async Task GetMe_UnknownAccount_ShouldAnswer401(string subject)
	{
		await using var harness = await SignInHarness.StartAsync(server);
		using var current = AccessTokenForger.ReadKey(harness.Factory.Secrets.CurrentKeyPem);

		var response = await harness.GetMeAsync(AccessTokenForger.Create(current, subject));

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// Verifies that during a rotation a token signed with the previous key is still accepted, and a token signed with a key
	/// that is neither the current nor the previous one is not.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMe_AfterRotation_ShouldAcceptThePreviousKeyOnly()
	{
		var previousPem = ApiSecrets.NewSigningKeyPem();
		var secrets = ApiSecrets.Generate() with { PreviousKeyPem = previousPem };
		await using var harness = await SignInHarness.StartAsync(server, secrets);
		var tokens = await harness.SignInAsync(Subject);
		var subject = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken).Subject;
		using var previous = AccessTokenForger.ReadKey(previousPem);
		using var retired = ECDsa.Create(ECCurve.NamedCurves.nistP256);

		var withPrevious = await harness.GetMeAsync(AccessTokenForger.Create(previous, subject));
		var withRetired = await harness.GetMeAsync(AccessTokenForger.Create(retired, subject));

		withPrevious.StatusCode.Should().Be(HttpStatusCode.OK);
		withRetired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken).Kid.Should().NotBe(AccessTokenForger.Thumbprint(previous), "new tokens are signed with the current key");
	}

	/// <summary>
	/// Verifies the per-account limit: a burst of 30 is served, the next request is answered 429 with Retry-After, and another
	/// account is not affected.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMe_BeyondTheBurst_ShouldAnswer429ForThatAccountOnly()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var busy = await harness.SignInAsync("google-subject-busy");
		var other = await harness.SignInAsync("google-subject-other");

		var statuses = new List<HttpStatusCode>();
		HttpResponseMessage? last = null;
		for (var request = 0; request < 40; request++)
		{
			last = await harness.GetMeAsync(busy.AccessToken);
			statuses.Add(last.StatusCode);
		}

		statuses.Count(status => status == HttpStatusCode.OK).Should().BeInRange(30, 34, "the bucket holds 30 and refills 2 a second");
		statuses.Should().Contain(HttpStatusCode.TooManyRequests);
		last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
		last.Headers.RetryAfter.Should().NotBeNull();
		(await harness.GetMeAsync(other.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}
	#endregion
}
