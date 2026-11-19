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
	#endregion
}
