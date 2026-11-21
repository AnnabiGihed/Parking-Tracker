using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Issues the access tokens of the API (ADR-0014): a JWT signed with ES256 under the current key, whose header carries
///              the key id, with the configured issuer, the audience <see cref="TokenOptions.Audience"/>, the account identifier
///              as <c>sub</c>, the family of the device session as <c>sid</c> (so a sign-out knows which device the caller holds) and a lifetime
///              of 15 minutes.
/// </summary>
public sealed class AccessTokenIssuer : IAccessTokenIssuer
{
	#region Constants
	/// <summary>
	/// The minutes an access token lives.
	/// </summary>
	public const int LifetimeMinutes = 15;

	/// <summary>
	/// The name of the claim that carries the family of the device session the token was issued for.
	/// </summary>
	public const string SessionClaim = "sid";
	#endregion

	#region Fields
	/// <summary>
	/// The signing keys.
	/// </summary>
	private readonly SigningKeyRing keys;

	/// <summary>
	/// The settings of the tokens.
	/// </summary>
	private readonly TokenOptions options;

	/// <summary>
	/// Writes and signs the tokens.
	/// </summary>
	private readonly JsonWebTokenHandler handler = new();
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccessTokenIssuer"/> class.
	/// </summary>
	/// <param name="keys">The signing keys.</param>
	/// <param name="options">The settings of the tokens.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public AccessTokenIssuer(SigningKeyRing keys, IOptions<TokenOptions> options)
	{
		ArgumentNullException.ThrowIfNull(keys);
		ArgumentNullException.ThrowIfNull(options);
		this.keys = keys;
		this.options = options.Value;
	}
	#endregion

	#region IAccessTokenIssuer Implementation
	/// <inheritdoc />
	public IssuedAccessToken Issue(UserAccountId accountId, Guid familyId, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(accountId);
		var issuedAt = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
		var expiresAt = issuedAt.AddMinutes(LifetimeMinutes);
		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = options.Issuer,
			Audience = TokenOptions.Audience,
			IssuedAt = issuedAt.UtcDateTime,
			NotBefore = issuedAt.UtcDateTime,
			Expires = expiresAt.UtcDateTime,
			Claims = new Dictionary<string, object>(StringComparer.Ordinal)
			{
				[JwtRegisteredClaimNames.Sub] = accountId.Value.ToString("D", CultureInfo.InvariantCulture),
				[SessionClaim] = familyId.ToString("D", CultureInfo.InvariantCulture),
			},
			SigningCredentials = new SigningCredentials(keys.Current, SecurityAlgorithms.EcdsaSha256),
		};
		return new IssuedAccessToken(handler.CreateToken(descriptor), expiresAt);
	}
	#endregion
}
