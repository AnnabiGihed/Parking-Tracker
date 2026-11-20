namespace ParkingTracker.Backend.Application.Features.Accounts.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The reason codes of a refused refresh. They go to the log only, never to the caller, and never carry the refresh
///              token or anything of the account.
/// </summary>
public static class RefreshFailureReason
{
	#region Constants
	/// <summary>
	/// The request carried no token, or one too long to be a refresh token.
	/// </summary>
	public const string TokenMalformed = "refresh.malformed";

	/// <summary>
	/// No session holds the token.
	/// </summary>
	public const string TokenUnknown = "refresh.unknown";

	/// <summary>
	/// The session of the token was revoked, by a sign-out, by a reuse or by the cap of devices.
	/// </summary>
	public const string SessionRevoked = "refresh.revoked";

	/// <summary>
	/// The family reached its sliding or its absolute expiry.
	/// </summary>
	public const string SessionExpired = "refresh.expired";

	/// <summary>
	/// A token that was exchanged before was presented again, so its family was revoked.
	/// </summary>
	public const string TokenReused = "refresh.reused";

	/// <summary>
	/// The rotation or the revocation could not be saved, for example because a second request rotated the same token first.
	/// </summary>
	public const string SaveFailed = "save.failed";

	/// <summary>
	/// The revocation of a family after a reuse could not be saved, not even a second time; the family stays usable until the next
	/// reuse or sign-out, and the operator should look at the database.
	/// </summary>
	public const string RevocationUnsaved = "refresh.revocation-unsaved";
	#endregion
}
