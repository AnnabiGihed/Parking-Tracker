using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Validates a Google ID token with the token handlers of Microsoft.IdentityModel (ADR-0014): signature against the
///              keys of the metadata document, RS256 only, the configured issuers and audiences, expiry with one minute of skew,
///              and the presence of the expiry, the subject and the nonce. The source of the keys is the injected configuration
///              manager, so a test replaces it with a static one holding a test key. A failure carries a reason code only, never the
///              token, a claim or the exception text.
/// </summary>
public sealed class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
	#region Constants
	/// <summary>
	/// The only signature algorithm Google signs ID tokens with.
	/// </summary>
	private const string Algorithm = SecurityAlgorithms.RsaSha256;

	/// <summary>
	/// The claim that carries the nonce.
	/// </summary>
	private const string NonceClaim = "nonce";
	#endregion

	#region Static Instances
	/// <summary>
	/// The skew allowed on the lifetime of a token.
	/// </summary>
	public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);
	#endregion

	#region Fields
	/// <summary>
	/// The source of the signing keys of Google.
	/// </summary>
	private readonly IConfigurationManager<OpenIdConnectConfiguration> configurationManager;

	/// <summary>
	/// The settings of the check.
	/// </summary>
	private readonly GoogleAuthenticationOptions options;

	/// <summary>
	/// Reads and validates the tokens.
	/// </summary>
	private readonly JsonWebTokenHandler handler = new();
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GoogleIdTokenValidator"/> class.
	/// </summary>
	/// <param name="configurationManager">The source of the signing keys of Google.</param>
	/// <param name="options">The settings of the check.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public GoogleIdTokenValidator(IConfigurationManager<OpenIdConnectConfiguration> configurationManager, IOptions<GoogleAuthenticationOptions> options)
	{
		ArgumentNullException.ThrowIfNull(configurationManager);
		ArgumentNullException.ThrowIfNull(options);
		this.configurationManager = configurationManager;
		this.options = options.Value;
	}
	#endregion

	#region IGoogleIdTokenValidator Implementation
	/// <inheritdoc />
	public async Task<Result<GoogleIdentity>> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(idToken);
		var configuration = await ReadKeysAsync(cancellationToken).ConfigureAwait(false);
		if (configuration is null)
		{
			return Fail(SignInFailureReason.KeysUnavailable);
		}

		var result = await handler.ValidateTokenAsync(idToken, ParametersFor(configuration)).ConfigureAwait(false);
		if (!result.IsValid)
		{
			if (result.Exception is SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException)
			{
				// Google rotates its keys, and a token under a new key id fails as a bad signature; the next attempt reads the new
				// document. The manager allows a refresh once per interval, so a flood of forged tokens costs one request.
				configurationManager.RequestRefresh();
			}

			return Fail(ReasonOf(result.Exception));
		}

		return result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var subject) && subject is string { Length: > 0 } subjectText
			&& result.Claims.TryGetValue(NonceClaim, out var nonce) && nonce is string { Length: > 0 } nonceText
			? new GoogleIdentity(subjectText, nonceText)
			: Fail(SignInFailureReason.TokenClaims);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the metadata document of Google, from the cache of the manager when it is fresh.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>The document, or <see langword="null"/> when it cannot be read.</returns>
	private async Task<OpenIdConnectConfiguration?> ReadKeysAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await configurationManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception problem) when (problem is InvalidOperationException or HttpRequestException or IOException or CryptographicException)
		{
			return null;
		}
	}

	/// <summary>
	/// Builds the validation rules for one check.
	/// </summary>
	/// <param name="configuration">The metadata document with the signing keys.</param>
	/// <returns>The rules.</returns>
	private TokenValidationParameters ParametersFor(OpenIdConnectConfiguration configuration)
	{
		return new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidIssuers = [.. options.ValidIssuers],
			ValidateAudience = true,
			ValidAudiences = [.. options.Audiences.Where(audience => !string.IsNullOrWhiteSpace(audience))],
			ValidateLifetime = true,
			RequireExpirationTime = true,
			ClockSkew = ClockSkew,
			RequireSignedTokens = true,
			ValidateIssuerSigningKey = true,
			IssuerSigningKeys = configuration.SigningKeys,
			ValidAlgorithms = [Algorithm],
		};
	}

	/// <summary>
	/// Maps the exception of a failed validation to a reason code.
	/// </summary>
	/// <param name="problem">The exception of the handler; <see langword="null"/> when it gave none.</param>
	/// <returns>The reason code.</returns>
	private static string ReasonOf(Exception? problem)
	{
		return problem switch
		{
			SecurityTokenInvalidSignatureException or SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidAlgorithmException => SignInFailureReason.TokenSignature,
			SecurityTokenInvalidIssuerException => SignInFailureReason.TokenIssuer,
			SecurityTokenInvalidAudienceException => SignInFailureReason.TokenAudience,
			SecurityTokenExpiredException or SecurityTokenNoExpirationException or SecurityTokenNotYetValidException or SecurityTokenInvalidLifetimeException => SignInFailureReason.TokenLifetime,
			SecurityTokenMalformedException or ArgumentException => SignInFailureReason.TokenMalformed,
			_ => SignInFailureReason.TokenInvalid,
		};
	}

	/// <summary>
	/// Builds the failure that carries a reason code.
	/// </summary>
	/// <param name="reason">The reason code.</param>
	/// <returns>The failure.</returns>
	private static Result<GoogleIdentity> Fail(string reason)
	{
		return Result.Failure<GoogleIdentity>(new Error(reason, "The ID token was refused."), ResultExceptionType.AuthenticationRequired);
	}
	#endregion
}
