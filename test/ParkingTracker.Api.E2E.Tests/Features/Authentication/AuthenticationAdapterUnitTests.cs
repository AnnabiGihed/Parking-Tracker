using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ParkingTracker.ApiService.Features.Authentication;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.Aggregates;

namespace ParkingTracker.Api.E2E.Tests.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests of the adapters of the sign-in without a host or a database: the subject hasher against an independent
///              HMAC, the access token issuer and the signing key ring against the rules of the token (algorithm, thumbprint as key
///              id, claims, lifetime), and the Google ID token validator against the test issuer, including the reason codes of
///              its failures and the refresh of the keys when a key id is unknown.
/// </summary>
public sealed class AuthenticationAdapterUnitTests
{
	#region Constants
	/// <summary>
	/// The subject hash key the tests use, base64 of 32 bytes.
	/// </summary>
	private const string HashKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
	#endregion

	#region Subject Hasher Tests
	/// <summary>
	/// Verifies the subject hash against an independent HMAC-SHA256: lowercase hexadecimal, 64 characters, and another key gives
	/// another hash.
	/// </summary>
	[Fact]
	public void SubjectHasher_KnownKey_ShouldBeTheLowercaseHexHmacOfTheSubject()
	{
		var hasher = new SubjectHasher(Options.Create(new AccountOptions { SubjectHashKey = HashKey }));
		var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromBase64String(HashKey), Encoding.UTF8.GetBytes("google-subject-1")));
		var otherKey = new SubjectHasher(Options.Create(new AccountOptions { SubjectHashKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

		var hash = hasher.Hash("google-subject-1");

		hash.Should().Be(expected).And.MatchRegex("^[0-9a-f]{64}$");
		hasher.Hash("google-subject-1").Should().Be(hash, "the hash is stable");
		otherKey.Hash("google-subject-1").Should().NotBe(hash);
	}

	/// <summary>
	/// Verifies that without a configured key (Development) the hasher still works, with a key of its own, and that an empty
	/// subject is rejected.
	/// </summary>
	[Fact]
	public void SubjectHasher_NoKeyConfigured_ShouldUseAProcessKeyAndRejectAnEmptySubject()
	{
		var hasher = new SubjectHasher(Options.Create(new AccountOptions()));

		hasher.Hash("a").Should().MatchRegex("^[0-9a-f]{64}$");
		var act = () => hasher.Hash(string.Empty);
		act.Should().Throw<ArgumentException>();
	}
	#endregion

	#region Access Token Issuer Tests
	/// <summary>
	/// Verifies the token the issuer writes: ES256, the thumbprint of the key as key id, issuer, audience, the account identifier
	/// as subject, the family of the session as sid and a lifetime of 15 minutes from the moment given.
	/// </summary>
	[Fact]
	public void AccessTokenIssuer_Issue_ShouldWriteAnEs256TokenForTheAccount()
	{
		var pem = ApiSecrets.NewSigningKeyPem();
		using var ring = new SigningKeyRing(new SigningKeyOptions { Current = pem });
		var issuer = new AccessTokenIssuer(ring, Options.Create(new TokenOptions { Issuer = "https://api.test.example" }));
		var accountId = UserAccountId.New();
		var familyId = Guid.NewGuid();
		var now = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
		using var key = AccessTokenForger.ReadKey(pem);

		var issued = issuer.Issue(accountId, familyId, now);

		var token = new JsonWebTokenHandler().ReadJsonWebToken(issued.Token);
		token.Alg.Should().Be("ES256");
		token.Kid.Should().Be(AccessTokenForger.Thumbprint(key));
		token.Issuer.Should().Be("https://api.test.example");
		token.Audiences.Should().ContainSingle().Which.Should().Be("parkingtracker-api");
		token.Subject.Should().Be(accountId.Value.ToString("D"));
		token.GetClaim(AccessTokenIssuer.SessionClaim).Value.Should().Be(familyId.ToString("D"), "the sign-out reads the family of the device from it");
		new DateTimeOffset(token.ValidFrom).Should().Be(now);
		issued.ExpiresAt.Should().Be(now.AddMinutes(15));
		new DateTimeOffset(token.ValidTo).Should().Be(issued.ExpiresAt);
	}
	#endregion

	#region Signing Key Ring Tests
	/// <summary>
	/// Verifies that the ring signs with the current key, validates with the current and the previous key, and gives them
	/// distinct key ids that are their thumbprints.
	/// </summary>
	[Fact]
	public void SigningKeyRing_CurrentAndPreviousKey_ShouldValidateWithBoth()
	{
		var current = ApiSecrets.NewSigningKeyPem();
		var previous = ApiSecrets.NewSigningKeyPem();

		using var ring = new SigningKeyRing(new SigningKeyOptions { Current = current, Previous = previous });

		ring.ValidationKeys.Should().HaveCount(2);
		ring.ValidationKeys[0].Should().BeSameAs(ring.Current);
		using var currentKey = AccessTokenForger.ReadKey(current);
		using var previousKey = AccessTokenForger.ReadKey(previous);
		ring.ValidationKeys.Select(key => key.KeyId).Should().Equal(AccessTokenForger.Thumbprint(currentKey), AccessTokenForger.Thumbprint(previousKey));
	}

	/// <summary>
	/// Verifies that the ring without any key makes one, and that a blank previous key is ignored.
	/// </summary>
	[Fact]
	public void SigningKeyRing_NoKeys_ShouldMakeACurrentKeyOnly()
	{
		using var ring = new SigningKeyRing(new SigningKeyOptions { Previous = "  " });

		ring.ValidationKeys.Should().ContainSingle();
		ring.Current.KeyId.Should().NotBeNullOrWhiteSpace();
	}

	/// <summary>
	/// Verifies the check of a key: a good one is accepted, an unusable one gives a reason that never carries the key.
	/// </summary>
	[Fact]
	public void SigningKeyRing_CheckGoodAndBadKeys_ShouldAcceptP256AndExplainWithoutShowingTheBadOne()
	{
		SigningKeyRing.Check(ApiSecrets.NewSigningKeyPem()).Should().BeNull();
		using var rsa = RSA.Create(2048);
		var rsaPem = rsa.ExportPkcs8PrivateKeyPem();

		var problem = SigningKeyRing.Check(rsaPem);

		problem.Should().NotBeNullOrWhiteSpace().And.NotContain(rsaPem[30..60]);
	}
	#endregion

	#region Previous Key Tests
	/// <summary>
	/// Verifies that only the current key signs: a ring whose previous key is set signs with the current one, a token signed with
	/// the previous key still validates with the ring, and a ring without a current key never signs with the previous one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SigningKeyRing_PreviousKeySet_ShouldValidateWithItButNeverSignWithIt()
	{
		var currentPem = ApiSecrets.NewSigningKeyPem();
		var previousPem = ApiSecrets.NewSigningKeyPem();
		using var currentKey = AccessTokenForger.ReadKey(currentPem);
		using var previousKey = AccessTokenForger.ReadKey(previousPem);
		var options = Options.Create(new TokenOptions { Issuer = "https://api.test.example" });
		using var both = new SigningKeyRing(new SigningKeyOptions { Current = currentPem, Previous = previousPem });
		using var onlyPrevious = new SigningKeyRing(new SigningKeyOptions { Previous = previousPem });

		var signedByBoth = new JsonWebTokenHandler().ReadJsonWebToken(new AccessTokenIssuer(both, options).Issue(UserAccountId.New(), Guid.NewGuid(), DateTimeOffset.UtcNow).Token);
		var signedByOnlyPrevious = new JsonWebTokenHandler().ReadJsonWebToken(new AccessTokenIssuer(onlyPrevious, options).Issue(UserAccountId.New(), Guid.NewGuid(), DateTimeOffset.UtcNow).Token);
		var oldToken = AccessTokenForger.Create(previousKey, "someone");
		var validation = await new JsonWebTokenHandler().ValidateTokenAsync(oldToken, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
		{
			ValidIssuer = ApiFactory.TokenIssuer,
			ValidAudience = AccessTokenForger.Audience,
			IssuerSigningKeys = both.ValidationKeys,
			ValidAlgorithms = [Microsoft.IdentityModel.Tokens.SecurityAlgorithms.EcdsaSha256],
		});

		signedByBoth.Kid.Should().Be(AccessTokenForger.Thumbprint(currentKey)).And.NotBe(AccessTokenForger.Thumbprint(previousKey));
		signedByOnlyPrevious.Kid.Should().NotBe(AccessTokenForger.Thumbprint(previousKey), "the previous key only validates");
		validation.IsValid.Should().BeTrue("a token signed with the previous key is still accepted during a rotation");
	}
	#endregion

	#region Google ID Token Validator Tests
	/// <summary>
	/// Verifies that a good ID token gives its subject and nonce, with the skew of one minute allowed on the expiry.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GoogleValidator_GoodTokenWithinSkew_ShouldGiveSubjectAndNonce()
	{
		using var issuer = new TestGoogleIssuer();
		var validator = NewValidator(issuer.CreateConfigurationManager());

		var fresh = await validator.ValidateAsync(issuer.CreateIdToken("sub-1", "nonce-1"));
		var justExpired = await validator.ValidateAsync(issuer.CreateIdToken("sub-1", "nonce-1", expires: DateTimeOffset.UtcNow.AddSeconds(-30)));
		var longExpired = await validator.ValidateAsync(issuer.CreateIdToken("sub-1", "nonce-1", expires: DateTimeOffset.UtcNow.AddMinutes(-3)));

		fresh.IsSuccess.Should().BeTrue();
		fresh.Value.Subject.Should().Be("sub-1");
		fresh.Value.Nonce.Should().Be("nonce-1");
		justExpired.IsSuccess.Should().BeTrue("one minute of skew is allowed");
		longExpired.Error.Code.Should().Be(SignInFailureReason.TokenLifetime);
	}

	/// <summary>
	/// Verifies the reason code of each failure, and that no reason or message carries the token.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GoogleValidator_BadTokenOfEachKind_ShouldGiveTheReasonCodeOnly()
	{
		using var issuer = new TestGoogleIssuer();
		var validator = NewValidator(issuer.CreateConfigurationManager());
		var cases = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[issuer.CreateIdToken("s", "n", audience: "other")] = SignInFailureReason.TokenAudience,
			[issuer.CreateIdToken("s", "n", issuer: "https://evil.example")] = SignInFailureReason.TokenIssuer,
			[issuer.CreateIdToken("s", "n", signedByStranger: true)] = SignInFailureReason.TokenSignature,
			[issuer.CreateIdToken("s", nonce: null)] = SignInFailureReason.TokenClaims,
			["not-a-token"] = SignInFailureReason.TokenMalformed,
		};

		foreach (var (token, reason) in cases)
		{
			var result = await validator.ValidateAsync(token);

			result.IsFailure.Should().BeTrue();
			result.Error.Code.Should().Be(reason);
			result.Error.Message.Should().NotContain(token);
		}
	}

	/// <summary>
	/// Verifies that a token made with another algorithm and a token without signature are refused as signature failures.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GoogleValidator_WrongAlgorithmOrNoSignature_ShouldFailOnTheSignature()
	{
		using var issuer = new TestGoogleIssuer();
		var validator = NewValidator(issuer.CreateConfigurationManager());

		var symmetric = await validator.ValidateAsync(issuer.CreateSymmetricIdToken("s", "n"));
		var unsigned = await validator.ValidateAsync(TestGoogleIssuer.CreateUnsignedIdToken("s", "n"));

		symmetric.IsFailure.Should().BeTrue();
		unsigned.Error.Code.Should().Be(SignInFailureReason.TokenSignature);
	}

	/// <summary>
	/// Verifies that when the keys of Google cannot be read the answer is a reason code and not an exception.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GoogleValidator_GoogleUnreachable_ShouldFailWithTheReasonCode()
	{
		using var issuer = new TestGoogleIssuer();
		var validator = NewValidator(new BrokenConfigurationManager());

		var result = await validator.ValidateAsync(issuer.CreateIdToken("s", "n"));

		result.Error.Code.Should().Be(SignInFailureReason.KeysUnavailable);
	}

	/// <summary>
	/// Verifies that a token under a key id that is not in the document asks the manager to read the document again, so that the
	/// next attempt sees the rotated keys of Google.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GoogleValidator_UnknownKeyId_ShouldRequestARefresh()
	{
		using var issuer = new TestGoogleIssuer();
		using var other = new TestGoogleIssuer();
		var manager = new CountingConfigurationManager(other.CreateConfiguration());
		var validator = NewValidator(manager);

		var result = await validator.ValidateAsync(issuer.CreateIdToken("s", "n"));

		result.Error.Code.Should().Be(SignInFailureReason.TokenSignature);
		manager.Refreshes.Should().BeGreaterThan(0);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a validator over a configuration manager, with the audience of the tests and the issuers of Google.
	/// </summary>
	/// <param name="manager">The source of the keys.</param>
	/// <returns>The validator.</returns>
	private static GoogleIdTokenValidator NewValidator(IConfigurationManager<OpenIdConnectConfiguration> manager)
	{
		return new GoogleIdTokenValidator(manager, Options.Create(new GoogleAuthenticationOptions { Audiences = [TestGoogleIssuer.Audience] }));
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : A configuration manager that cannot read the document, as when Google is unreachable.
	/// </summary>
	private sealed class BrokenConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
	{
		#region IConfigurationManager Implementation
		/// <inheritdoc />
		public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
		{
			throw new InvalidOperationException("Google cannot be reached.");
		}

		/// <inheritdoc />
		public void RequestRefresh()
		{
		}
		#endregion
	}

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : A configuration manager with a fixed document that counts the requests to read it again.
	/// </summary>
	private sealed class CountingConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
	{
		#region Fields
		/// <summary>
		/// The document.
		/// </summary>
		private readonly OpenIdConnectConfiguration configuration;
		#endregion

		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="CountingConfigurationManager"/> class.
		/// </summary>
		/// <param name="configuration">The document.</param>
		public CountingConfigurationManager(OpenIdConnectConfiguration configuration)
		{
			this.configuration = configuration;
		}
		#endregion

		#region Properties
		/// <summary>
		/// Gets the number of requests to read the document again.
		/// </summary>
		public int Refreshes { get; private set; }
		#endregion

		#region IConfigurationManager Implementation
		/// <inheritdoc />
		public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
		{
			return Task.FromResult(configuration);
		}

		/// <inheritdoc />
		public void RequestRefresh()
		{
			Refreshes++;
		}
		#endregion
	}
	#endregion
}
