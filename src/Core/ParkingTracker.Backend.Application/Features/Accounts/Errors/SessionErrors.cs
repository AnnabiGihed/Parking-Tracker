using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of the sign-in, the refresh and the account read. The sign-in and the refresh have one error for every cause, so the answer tells a
///              caller nothing about which check failed; the cause goes to the log as a reason code.
/// </summary>
public static class SessionErrors
{
	#region Static Instances
	/// <summary>
	/// The sign-in was refused, whatever the reason.
	/// </summary>
	public static readonly Error SignInFailed = new("Session.SignIn.Failed", "The sign-in could not be completed.");

	/// <summary>
	/// The refresh was refused, whatever the reason.
	/// </summary>
	public static readonly Error RefreshFailed = new("Session.Refresh.Failed", "The session could not be refreshed.");

	/// <summary>
	/// The access token names no device session, so there is no device to sign out.
	/// </summary>
	public static readonly Error SessionUnknown = new("Session.Unknown", "The access token names no session.");

	/// <summary>
	/// The account of a valid access token does not exist (any more).
	/// </summary>
	public static readonly Error AccountUnknown = new("Account.Unknown", "The account is not known.");
	#endregion
}
