using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Configures JWT bearer authentication against the API own keys (ADR-0014): ES256 only, signed tokens only, the
///              configured issuer and the audience <see cref="TokenOptions.Audience"/> checked, 30 seconds of skew, the current and
///              previous key accepted (so a rotation does not sign everyone out), and claims left as they are in the token
///              (<c>sub</c> stays <c>sub</c>). It reads the keys from the registered ring, so the validation and the issuing share one
///              source.
/// </summary>
public sealed class ApiJwtBearerConfigurator : IConfigureNamedOptions<JwtBearerOptions>
{
	#region Static Instances
	/// <summary>
	/// The skew allowed on the lifetime of an access token.
	/// </summary>
	public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
	#endregion

	#region Fields
	/// <summary>
	/// The signing keys.
	/// </summary>
	private readonly SigningKeyRing keys;

	/// <summary>
	/// The settings of the tokens.
	/// </summary>
	private readonly TokenOptions tokens;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ApiJwtBearerConfigurator"/> class.
	/// </summary>
	/// <param name="keys">The signing keys.</param>
	/// <param name="tokens">The settings of the tokens.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ApiJwtBearerConfigurator(SigningKeyRing keys, IOptions<TokenOptions> tokens)
	{
		ArgumentNullException.ThrowIfNull(keys);
		ArgumentNullException.ThrowIfNull(tokens);
		this.keys = keys;
		this.tokens = tokens.Value;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public void Configure(string? name, JwtBearerOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
		{
			return;
		}

		options.MapInboundClaims = false;
		options.SaveToken = false;
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidIssuer = tokens.Issuer,
			ValidateAudience = true,
			ValidAudience = TokenOptions.Audience,
			ValidateLifetime = true,
			RequireExpirationTime = true,
			ClockSkew = ClockSkew,
			RequireSignedTokens = true,
			ValidateIssuerSigningKey = true,
			IssuerSigningKeys = keys.ValidationKeys,
			ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
			NameClaimType = JwtRegisteredClaimNames.Sub,
		};
	}

	/// <inheritdoc />
	public void Configure(JwtBearerOptions options)
	{
		Configure(Options.DefaultName, options);
	}
	#endregion
}
