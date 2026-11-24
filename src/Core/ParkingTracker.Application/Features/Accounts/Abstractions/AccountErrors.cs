using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The failures of signing in and of the calls made as a signed-in user (ADR-0014, ADR-0015). Each has a stable code, so that
///              the code can tell them apart, and a message in plain words, so that a screen can show it as it is. The three
///              Google failures are distinct because the user does something different about each: nothing for a cancel, adds an account
///              for a missing one, and nothing at all for an unavailable one.
/// </summary>
public static class AccountErrors
{
	#region Constants
	/// <summary>
	/// The user left the Google account chooser without choosing an account.
	/// </summary>
	public const string CancelledCode = "Account.SignIn.Cancelled";

	/// <summary>
	/// The phone has no Google account to sign in with.
	/// </summary>
	public const string NoAccountCode = "Account.SignIn.NoAccount";

	/// <summary>
	/// Google sign-in cannot be used in this build or on this phone.
	/// </summary>
	public const string UnavailableCode = "Account.SignIn.Unavailable";

	/// <summary>
	/// There is no session, or the server refused it and the refresh token: the user is signed out.
	/// </summary>
	public const string NotSignedInCode = "Account.NotSignedIn";

	/// <summary>
	/// The server could not be reached, or the connection broke; the session is kept.
	/// </summary>
	public const string NetworkCode = "Account.Network";

	/// <summary>
	/// The server refused the request, for example the Google ID token.
	/// </summary>
	public const string RefusedCode = "Account.Refused";

	/// <summary>
	/// The server answered with an error or with something the app cannot read; the session is kept.
	/// </summary>
	public const string ServerCode = "Account.Server";
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the failure for a user who left the account chooser.
	/// </summary>
	/// <returns>The error with the code <see cref="CancelledCode"/>.</returns>
	public static Error Cancelled() => new(CancelledCode, "Sign-in was cancelled.");

	/// <summary>
	/// Builds the failure for a phone without a Google account.
	/// </summary>
	/// <returns>The error with the code <see cref="NoAccountCode"/>.</returns>
	public static Error NoAccount() => new(NoAccountCode, "There is no Google account on this phone. Add one in the phone settings and try again.");

	/// <summary>
	/// Builds the failure for Google sign-in that cannot be used.
	/// </summary>
	/// <returns>The error with the code <see cref="UnavailableCode"/>.</returns>
	public static Error Unavailable() => new(UnavailableCode, "Google sign-in is not available in this build or on this phone.");

	/// <summary>
	/// Builds the failure for a user who is not signed in.
	/// </summary>
	/// <returns>The error with the code <see cref="NotSignedInCode"/>.</returns>
	public static Error NotSignedIn() => new(NotSignedInCode, "You are not signed in.");

	/// <summary>
	/// Builds the failure for a server that could not be reached.
	/// </summary>
	/// <returns>The error with the code <see cref="NetworkCode"/>.</returns>
	public static Error Network() => new(NetworkCode, "The server could not be reached. Check your connection and try again.");

	/// <summary>
	/// Builds the failure for a request the server refused.
	/// </summary>
	/// <returns>The error with the code <see cref="RefusedCode"/>.</returns>
	public static Error Refused() => new(RefusedCode, "The server refused the request. Try again.");

	/// <summary>
	/// Builds the failure for a server that answered with an error.
	/// </summary>
	/// <returns>The error with the code <see cref="ServerCode"/>.</returns>
	public static Error Server() => new(ServerCode, "The server had a problem. Try again later.");
	#endregion
}
