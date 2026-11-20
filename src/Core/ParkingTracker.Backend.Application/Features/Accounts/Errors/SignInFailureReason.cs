namespace ParkingTracker.Backend.Application.Features.Accounts.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The reason codes of a refused sign-in. They go to the log only, never to the caller, and never carry the token, the
///              subject or the nonce. The token validator returns the token.* codes as the code of its failure.
/// </summary>
public static class SignInFailureReason
{
	#region Constants
	/// <summary>
	/// The request carried no token, or one too long to be an ID token.
	/// </summary>
	public const string TokenMissing = "token.missing";

	/// <summary>
	/// The token is not a JWT that can be read.
	/// </summary>
	public const string TokenMalformed = "token.malformed";

	/// <summary>
	/// The signature is wrong, missing, made with an unknown key or with an algorithm other than RS256.
	/// </summary>
	public const string TokenSignature = "token.signature";

	/// <summary>
	/// The issuer is not the one of Google.
	/// </summary>
	public const string TokenIssuer = "token.issuer";

	/// <summary>
	/// The audience is not one of the configured client identifiers.
	/// </summary>
	public const string TokenAudience = "token.audience";

	/// <summary>
	/// The token expired, or has no expiry.
	/// </summary>
	public const string TokenLifetime = "token.lifetime";

	/// <summary>
	/// The token has no subject or no nonce.
	/// </summary>
	public const string TokenClaims = "token.claims";

	/// <summary>
	/// The signing keys of Google could not be read.
	/// </summary>
	public const string KeysUnavailable = "token.keys-unavailable";

	/// <summary>
	/// The token failed another check.
	/// </summary>
	public const string TokenInvalid = "token.invalid";

	/// <summary>
	/// The nonce was never issued, was used already or expired.
	/// </summary>
	public const string NonceRejected = "nonce.rejected";

	/// <summary>
	/// The account or its session could not be saved.
	/// </summary>
	public const string SaveFailed = "save.failed";

	/// <summary>
	/// A hash did not have the expected shape; a defect of the host configuration or code.
	/// </summary>
	public const string HashInvalid = "hash.invalid";
	#endregion
}
