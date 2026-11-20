using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of the sign-in and the account read. The sign-in has one error for every cause, so the answer tells a
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
	/// The account of a valid access token does not exist (any more).
	/// </summary>
	public static readonly Error AccountUnknown = new("Account.Unknown", "The account is not known.");
	#endregion
}
