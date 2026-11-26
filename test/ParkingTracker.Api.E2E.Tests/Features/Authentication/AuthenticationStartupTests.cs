using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ParkingTracker.Api.E2E.Tests.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests of the start-up checks of the sign-in settings (ADR-0014, ADR-0025): outside Development the API refuses to
///              start without a usable signing key, subject hash key or profile key, with a key that is not a P-256 private key, with settings
///              that point at a test issuer instead of Google, without an audience or with an issuer that is not https, and the
///              messages name the configuration key and never carry a key. Development starts without any of them. The tests
///              need no database.
/// </summary>
public sealed class AuthenticationStartupTests
{
	#region Constants
	/// <summary>
	/// A connection string to a port nothing listens on; these tests never reach the database.
	/// </summary>
	private const string UnreachableConnection = "Host=127.0.0.1;Port=1;Database=parking;Username=u;Password=p;Timeout=2";

	/// <summary>
	/// A metadata address of a test issuer.
	/// </summary>
	private const string TestIssuerMetadata = "https://test-issuer.example/.well-known/openid-configuration";
	#endregion

	#region Secret Tests
	/// <summary>
	/// Verifies that Production refuses to start without a current signing key.
	/// </summary>
	[Fact]
	public void Start_ProductionWithoutSigningKey_ShouldFailNamingTheKey()
	{
		using var factory = new ApiFactory("Production", UnreachableConnection, secrets: ApiSecrets.Generate() with { CurrentKeyPem = string.Empty });

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().WithMessage("*Tokens:SigningKeys:Current*");
	}

	/// <summary>
	/// Verifies that Production refuses to start without a subject hash key.
	/// </summary>
	[Fact]
	public void Start_ProductionWithoutSubjectHashKey_ShouldFailNamingTheKey()
	{
		using var factory = new ApiFactory("Production", UnreachableConnection, secrets: ApiSecrets.Generate() with { SubjectHashKey = string.Empty });

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().WithMessage("*Accounts:SubjectHashKey*");
	}

	/// <summary>
	/// Verifies that a subject hash key that is not base64 of 32 bytes is refused, and that the message does not carry it.
	/// </summary>
	/// <param name="key">The key text.</param>
	[Theory]
	[InlineData("not base64 at all !!")]
	[InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
	public void Start_ProductionWithAWrongSubjectHashKey_ShouldFailWithoutShowingTheKey(string key)
	{
		using var factory = new ApiFactory("Production", UnreachableConnection, secrets: ApiSecrets.Generate() with { SubjectHashKey = key });

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().Where(problem => problem.Message.Contains("Accounts:SubjectHashKey", StringComparison.Ordinal) && !problem.Message.Contains(key, StringComparison.Ordinal));
	}

	/// <summary>
	/// Verifies that Production refuses to start without a current profile key.
	/// </summary>
	[Fact]
	public void Start_ProductionWithoutProfileKey_ShouldFailNamingTheKey()
	{
		using var factory = new ApiFactory("Production", UnreachableConnection, secrets: ApiSecrets.Generate() with { ProfileKeyCurrent = string.Empty });

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().WithMessage("*Profiles:Keys:Current*");
	}

	/// <summary>
	/// Verifies that a previous profile key that is the current key is refused, without showing the key.
	/// </summary>
	[Fact]
	public void Start_ProductionWithThePreviousProfileKeyEqualToTheCurrent_ShouldFailWithoutShowingTheKey()
	{
		var secrets = ApiSecrets.Generate();
		using var factory = new ApiFactory("Production", UnreachableConnection, secrets: secrets with { ProfileKeyPrevious = secrets.ProfileKeyCurrent });

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().Where(problem => problem.Message.Contains("Profiles:Keys:Previous", StringComparison.Ordinal) && !problem.Message.Contains(secrets.ProfileKeyCurrent, StringComparison.Ordinal));
	}

	/// <summary>
	/// Verifies that a current or previous profile key that is not base64 of 32 bytes is refused, and that the message does not carry it.
	/// </summary>
	/// <param name="key">The key text.</param>
	/// <param name="current">Whether the bad key is the current key (otherwise the previous one).</param>
	[Theory]
	[InlineData("not base64 at all !!", true)]
	[InlineData("AAAAAAAAAAAAAAAAAAAAAA==", true)]
	[InlineData("not base64 at all !!", false)]
	[InlineData("AAAAAAAAAAAAAAAAAAAAAA==", false)]
	public void Start_ProductionWithAWrongProfileKey_ShouldFailWithoutShowingTheKey(string key, bool current)
	{
		var secrets = ApiSecrets.Generate();
		using var factory = new ApiFactory("Production", UnreachableConnection, secrets: current ? secrets with { ProfileKeyCurrent = key } : secrets with { ProfileKeyPrevious = key });

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().Where(problem => problem.Message.Contains("Profiles:Keys:", StringComparison.Ordinal) && !problem.Message.Contains(key, StringComparison.Ordinal));
	}

	/// <summary>
	/// Verifies that a signing key that is not a P-256 private key in PEM is refused: garbage, a key on another curve and an RSA
	/// key, as the current key and as the previous one, and that the message never carries the key.
	/// </summary>
	/// <param name="kind">The kind of unusable key.</param>
	/// <param name="current">Whether the unusable key is the current key (otherwise the previous one).</param>
	[Theory]
	[InlineData("garbage", true)]
	[InlineData("p384", true)]
	[InlineData("rsa", true)]
	[InlineData("garbage", false)]
	[InlineData("p384", false)]
	public void Start_ProductionWithAnUnusableSigningKey_ShouldFailWithoutShowingTheKey(string kind, bool current)
	{
		var bad = NewUnusableKey(kind);
		var secrets = current ? ApiSecrets.Generate() with { CurrentKeyPem = bad } : ApiSecrets.Generate() with { PreviousKeyPem = bad };
		using var factory = new ApiFactory("Production", UnreachableConnection, secrets: secrets);

		var act = () => factory.CreateClient();

		var which = current ? "Current" : "Previous";
		var fragment = bad[..40];
		act.Should().Throw<OptionsValidationException>().Where(problem => problem.Message.Contains($"Tokens:SigningKeys:{which}", StringComparison.Ordinal) && !problem.Message.Contains(fragment, StringComparison.Ordinal));
	}
	#endregion

	#region Google Settings Tests
	/// <summary>
	/// Verifies that Production refuses a test issuer: a metadata address that is not the one of Google.
	/// </summary>
	[Fact]
	public void Start_ProductionWithATestIssuerAddress_ShouldFail()
	{
		var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Authentication:Google:MetadataAddress"] = TestIssuerMetadata };
		using var factory = new ApiFactory("Production", UnreachableConnection, settings: settings);

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().WithMessage("*Authentication:Google:MetadataAddress*");
	}

	/// <summary>
	/// Verifies that Production refuses to accept the issuer of a test as one of the valid issuers.
	/// </summary>
	[Fact]
	public void Start_ProductionWithAnExtraIssuer_ShouldFail()
	{
		var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Authentication:Google:ValidIssuers:0"] = "https://test-issuer.example" };
		using var factory = new ApiFactory("Production", UnreachableConnection, settings: settings);

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().WithMessage("*Authentication:Google:ValidIssuers*");
	}

	/// <summary>
	/// Verifies that Production refuses to start without a client identifier to accept.
	/// </summary>
	[Fact]
	public void Start_ProductionWithoutAnAudience_ShouldFail()
	{
		var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Authentication:Google:Audiences:0"] = string.Empty };
		using var factory = new ApiFactory("Production", UnreachableConnection, settings: settings);

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().WithMessage("*Authentication:Google:Audiences*");
	}

	/// <summary>
	/// Verifies that Production refuses an issuer of the access tokens that is not an https address.
	/// </summary>
	/// <param name="issuer">The issuer.</param>
	[Theory]
	[InlineData("http://api.test.example")]
	[InlineData("api.test.example")]
	[InlineData("")]
	public void Start_ProductionWithANonHttpsTokenIssuer_ShouldFail(string issuer)
	{
		var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tokens:Issuer"] = issuer };
		using var factory = new ApiFactory("Production", UnreachableConnection, settings: settings);

		var act = () => factory.CreateClient();

		act.Should().Throw<OptionsValidationException>().WithMessage("*Tokens:Issuer*");
	}
	#endregion

	#region Development Tests
	/// <summary>
	/// Verifies that Development starts without any of the secrets, with keys made for the process, and that the sign-in endpoints
	/// are reachable (the challenge needs the database, so only the anonymous health path and the protected path are checked).
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Start_DevelopmentWithoutSecrets_ShouldStartAndProtectTheApi()
	{
		await using var factory = new ApiFactory("Development", UnreachableConnection, secrets: ApiSecrets.None);
		using var client = factory.CreateClient();

		var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
		var me = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative));

		live.StatusCode.Should().Be(HttpStatusCode.OK);
		me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}
	#endregion

	#region Pipeline Tests
	/// <summary>
	/// Verifies that the fallback policy does not turn a path that does not exist into a 401: it stays a 404, and the health
	/// endpoints stay open without a token.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Request_UnknownPathWithoutToken_ShouldAnswer404AndHealthStaysOpen()
	{
		await using var factory = new ApiFactory("Production", UnreachableConnection);
		using var client = factory.CreateClient();

		var unknown = await client.GetAsync(new Uri("/no/such/path", UriKind.Relative));
		var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

		unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
		live.StatusCode.Should().Be(HttpStatusCode.OK);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Makes an unusable signing key of a kind.
	/// </summary>
	/// <param name="kind">garbage, p384 or rsa.</param>
	/// <returns>PEM text, at least 40 characters long.</returns>
	private static string NewUnusableKey(string kind)
	{
		switch (kind)
		{
			case "p384":
				using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP384))
				{
					return key.ExportPkcs8PrivateKeyPem();
				}

			case "rsa":
				using (var key = RSA.Create(2048))
				{
					return key.ExportPkcs8PrivateKeyPem();
				}

			default:
				return "-----BEGIN PRIVATE KEY-----\nthis-is-not-a-key-at-all-just-text\n-----END PRIVATE KEY-----";
		}
	}
	#endregion
}
