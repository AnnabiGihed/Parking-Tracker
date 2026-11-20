using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that checks a Google ID token: signature against Google's published keys, issuer, audience, expiry and the
///              presence of the subject and the nonce. It does not check that the nonce was issued; the handler does.
/// </summary>
public interface IGoogleIdTokenValidator
{
	#region Methods
	/// <summary>
	/// Validates an ID token.
	/// </summary>
	/// <param name="idToken">The ID token as the app sent it.</param>
	/// <param name="cancellationToken">Cancels the validation.</param>
	/// <returns>A task with the subject and the nonce, or a failure whose error code is a short reason code (never the token or a claim).</returns>
	Task<Result<GoogleIdentity>> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
	#endregion
}
