using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ParkingTracker.Api.E2E.Tests.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Writes access tokens the way the API does, but under the control of a test: any key, issuer, audience, subject and
///              expiry, and the two forgeries that are classic, no signature and a shared-secret signature. The thumbprint of a key
///              is computed here from the RFC 7638 rule, independently of the code under test.
/// </summary>
public static class AccessTokenForger
{
	#region Constants
	/// <summary>
	/// The audience of the access tokens of the API.
	/// </summary>
	public const string Audience = "parkingtracker-api";
	#endregion

	#region Public Methods
	/// <summary>
	/// Writes an ES256 access token.
	/// </summary>
	/// <param name="key">The key to sign with.</param>
	/// <param name="subject">The subject, normally an account identifier.</param>
	/// <param name="issuer">The issuer; the one of the test host when omitted.</param>
	/// <param name="audience">The audience; the one of the API when omitted.</param>
	/// <param name="expires">The expiry; 15 minutes from now when omitted.</param>
	/// <param name="keyId">The key id of the header; the thumbprint of <paramref name="key"/> when omitted.</param>
	/// <param name="familyId">The family of the device session for the sid claim; no sid claim when omitted.</param>
	/// <returns>The signed token.</returns>
	public static string Create(
		ECDsa key,
		string subject,
		string issuer = ApiFactory.TokenIssuer,
		string audience = Audience,
		DateTimeOffset? expires = null,
		string? keyId = null,
		Guid? familyId = null)
	{
		ArgumentNullException.ThrowIfNull(key);
		var expiry = expires ?? DateTimeOffset.UtcNow.AddMinutes(15);
		var claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = subject };
		if (familyId is { } family)
		{
			claims["sid"] = family.ToString("D");
		}

		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = issuer,
			Audience = audience,
			IssuedAt = expiry.AddMinutes(-15).UtcDateTime,
			NotBefore = expiry.AddMinutes(-15).UtcDateTime,
			Expires = expiry.UtcDateTime,
			Claims = claims,
			SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = keyId ?? Thumbprint(key) }, SecurityAlgorithms.EcdsaSha256),
		};
		return new JsonWebTokenHandler().CreateToken(descriptor);
	}

	/// <summary>
	/// Writes a token that claims the algorithm none and has no signature.
	/// </summary>
	/// <param name="subject">The subject.</param>
	/// <returns>The unsigned token.</returns>
	public static string CreateUnsigned(string subject)
	{
		var header = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new { alg = "none", typ = "JWT" }));
		var payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new
		{
			iss = ApiFactory.TokenIssuer,
			aud = Audience,
			sub = subject,
			exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(),
		}));
		return $"{header}.{payload}.";
	}

	/// <summary>
	/// Writes a token signed with HMAC-SHA256 under a shared secret.
	/// </summary>
	/// <param name="subject">The subject.</param>
	/// <returns>The token.</returns>
	public static string CreateSymmetric(string subject)
	{
		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = ApiFactory.TokenIssuer,
			Audience = Audience,
			Expires = DateTime.UtcNow.AddMinutes(15),
			Claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = subject },
			SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-shared-secret-of-at-least-32-bytes!!")), SecurityAlgorithms.HmacSha256),
		};
		return new JsonWebTokenHandler().CreateToken(descriptor);
	}

	/// <summary>
	/// Reads a P-256 private key from PEM text.
	/// </summary>
	/// <param name="pem">The PEM text.</param>
	/// <returns>The key; the caller disposes it.</returns>
	public static ECDsa ReadKey(string pem)
	{
		var key = ECDsa.Create();
		key.ImportFromPem(pem);
		return key;
	}

	/// <summary>
	/// Computes the JWK thumbprint of an EC key by the rule of RFC 7638: SHA-256 of the required members in order, base64url.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <returns>The thumbprint.</returns>
	public static string Thumbprint(ECDsa key)
	{
		ArgumentNullException.ThrowIfNull(key);
		var point = key.ExportParameters(false).Q;
		var json = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{Base64UrlEncoder.Encode(point.X)}\",\"y\":\"{Base64UrlEncoder.Encode(point.Y)}\"}}";
		return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
	}
	#endregion
}
