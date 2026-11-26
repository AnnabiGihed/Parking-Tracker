using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.Accounts.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of the user account and its device sessions; each code is stable and dotted.
/// </summary>
public static class AccountErrors
{
	#region Static Instances
	/// <summary>
	/// The subject hash is not 64 lowercase hexadecimal characters.
	/// </summary>
	public static readonly Error SubjectHashInvalid = new("Account.SubjectHash.Invalid", "A subject hash is 64 lowercase hexadecimal characters.");

	/// <summary>
	/// The refresh token hash is not 64 lowercase hexadecimal characters.
	/// </summary>
	public static readonly Error RefreshTokenHashInvalid = new("Account.RefreshTokenHash.Invalid", "A refresh token hash is 64 lowercase hexadecimal characters.");

	/// <summary>
	/// The account has no device session with the refresh token hash.
	/// </summary>
	public static readonly Error SessionNotFound = new("Account.Session.NotFound", "The account has no session with this refresh token.");

	/// <summary>
	/// The device session was revoked, by a sign-out or by the reuse of a token of its family.
	/// </summary>
	public static readonly Error SessionRevoked = new("Account.Session.Revoked", "The session was revoked; sign in again.");

	/// <summary>
	/// The device session reached its absolute or its sliding expiry.
	/// </summary>
	public static readonly Error SessionExpired = new("Account.Session.Expired", "The session has expired; sign in again.");

	/// <summary>
	/// A refresh token that was rotated already was presented again, so its whole family is revoked.
	/// </summary>
	public static readonly Error RefreshTokenReused = new("Account.RefreshToken.Reused", "The refresh token was used before; every session of its family is revoked.");

	/// <summary>
	/// The encrypted profile has no payload.
	/// </summary>
	public static readonly Error ProfilePayloadEmpty = new("Account.Profile.PayloadEmpty", "An encrypted profile has a payload.");

	/// <summary>
	/// The encrypted profile is larger than a profile of 32 KB of plain text can be.
	/// </summary>
	public static readonly Error ProfilePayloadTooLarge = new("Account.Profile.PayloadTooLarge", "The encrypted profile is larger than a profile of 32 KB can be.");

	/// <summary>
	/// The key identifier is not 8 lowercase hexadecimal characters.
	/// </summary>
	public static readonly Error ProfileKeyIdInvalid = new("Account.Profile.KeyIdInvalid", "A profile key identifier is 8 lowercase hexadecimal characters.");

	/// <summary>
	/// The schema version of the profile document is not a positive number.
	/// </summary>
	public static readonly Error ProfileSchemaVersionInvalid = new("Account.Profile.SchemaVersionInvalid", "The schema version of a profile is 1 or more.");
	#endregion
}
