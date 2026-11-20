using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A stand-in for Google (ADR-0025): an issuer with its own RSA key that signs ID tokens the way Google does and
///              publishes its key as the metadata document the API reads, through a static configuration manager that a test puts
///              in the container. It can also forge every kind of bad token the exchange must refuse: another audience or issuer, an
///              expired token, a missing nonce, no signature, a wrong signature and another algorithm.
/// </summary>
public sealed class TestGoogleIssuer : IDisposable
{
	#region Constants
	/// <summary>
	/// The client identifier the API is configured to accept in tests.
	/// </summary>
	public const string Audience = "test-web-client.apps.googleusercontent.com";

	/// <summary>
	/// The issuer Google signs with; the API checks it by name, whatever source the keys come from.
	/// </summary>
	public const string Issuer = "https://accounts.google.com";

	/// <summary>
	/// The key id of the test key.
	/// </summary>
	public const string KeyId = "test-google-key-1";
	#endregion

	#region Fields
	/// <summary>
	/// The key that signs the ID tokens.
	/// </summary>
	private readonly RSA signingKey = RSA.Create(2048);

	/// <summary>
	/// A key the API does not know, to forge a signature.
	/// </summary>
	private readonly RSA strangerKey = RSA.Create(2048);

	/// <summary>
	/// Writes the tokens.
	/// </summary>
	private readonly JsonWebTokenHandler handler = new();
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the metadata document the API reads in place of the one of Google, with the public part of the test key.
	/// </summary>
	/// <returns>The document.</returns>
	public OpenIdConnectConfiguration CreateConfiguration()
	{
		var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
		var publicKey = new RsaSecurityKey(signingKey.ExportParameters(false)) { KeyId = KeyId };
		var jsonWebKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(publicKey);
		jsonWebKey.Use = "sig";
		jsonWebKey.Alg = SecurityAlgorithms.RsaSha256;
		configuration.JsonWebKeySet = new JsonWebKeySet();
		configuration.JsonWebKeySet.Keys.Add(jsonWebKey);
		configuration.SigningKeys.Add(jsonWebKey);
		return configuration;
	}

	/// <summary>
	/// Builds a static configuration manager holding the test key, to put in the container in place of the one that calls Google.
	/// </summary>
	/// <returns>The manager.</returns>
	public IConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager()
	{
		return new StaticConfigurationManager<OpenIdConnectConfiguration>(CreateConfiguration());
	}

	/// <summary>
	/// Issues an ID token as Google would, valid for one hour; every argument can be bent to forge a bad one.
	/// </summary>
	/// <param name="subject">The subject.</param>
	/// <param name="nonce">The nonce; <see langword="null"/> for none.</param>
	/// <param name="audience">The audience; the configured one when omitted.</param>
	/// <param name="issuer">The issuer; Google when omitted.</param>
	/// <param name="expires">The expiry; one hour from now when omitted.</param>
	/// <param name="signedByStranger">Whether to sign with a key the API does not know, under the key id of the test key.</param>
	/// <returns>The signed token.</returns>
	public string CreateIdToken(
		string subject,
		string? nonce,
		string audience = Audience,
		string issuer = Issuer,
		DateTimeOffset? expires = null,
		bool signedByStranger = false)
	{
		var expiry = expires ?? DateTimeOffset.UtcNow.AddHours(1);
		var claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = subject, ["email"] = "driver@example.test" };
		if (nonce is not null)
		{
			claims["nonce"] = nonce;
		}

		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = issuer,
			Audience = audience,
			IssuedAt = expiry.AddHours(-1).UtcDateTime,
			NotBefore = expiry.AddHours(-1).UtcDateTime,
			Expires = expiry.UtcDateTime,
			Claims = claims,
			SigningCredentials = new SigningCredentials(new RsaSecurityKey(signedByStranger ? strangerKey : signingKey) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
		};
		return handler.CreateToken(descriptor);
	}

	/// <summary>
	/// Builds a token with the algorithm "none" and no signature, with the claims of a good one.
	/// </summary>
	/// <param name="subject">The subject.</param>
	/// <param name="nonce">The nonce.</param>
	/// <returns>The unsigned token.</returns>
	public static string CreateUnsignedIdToken(string subject, string nonce)
	{
		var header = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new { alg = "none", typ = "JWT" }));
		var payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new
		{
			iss = Issuer,
			aud = Audience,
			sub = subject,
			nonce,
			exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
		}));
		return $"{header}.{payload}.";
	}

	/// <summary>
	/// Builds a token signed with HMAC-SHA256 under a shared secret, the classic algorithm-confusion forgery.
	/// </summary>
	/// <param name="subject">The subject.</param>
	/// <param name="nonce">The nonce.</param>
	/// <returns>The token.</returns>
	public string CreateSymmetricIdToken(string subject, string nonce)
	{
		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = Issuer,
			Audience = Audience,
			Expires = DateTime.UtcNow.AddHours(1),
			Claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = subject, ["nonce"] = nonce },
			SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-shared-secret-of-at-least-32-bytes!!")) { KeyId = KeyId }, SecurityAlgorithms.HmacSha256),
		};
		return handler.CreateToken(descriptor);
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		signingKey.Dispose();
		strangerKey.Dispose();
	}
	#endregion
}
