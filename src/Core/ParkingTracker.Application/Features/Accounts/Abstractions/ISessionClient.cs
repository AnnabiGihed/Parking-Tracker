using ParkingTracker.Contracts.Features.Accounts;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the account of the user on the API (ADR-0014, ADR-0015): sign-in with Google, the account details, sign-out and
///              deletion of the account. It owns the tokens: calls are authenticated, the access token is refreshed before it expires or
///              when the API refuses it, and only a refresh that the API rejects signs the user out; a network error never does. The
///              failures use the codes of <see cref="AccountErrors"/>.
/// </summary>
/// <remarks>Deleting the account removes it on the server only. Wiping the data on the phone after an erasure belongs to the mandatory sign-in stage (S9).</remarks>
public interface ISessionClient
{
	#region Methods
	/// <summary>
	/// Tells whether the phone holds a session.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with <see langword="true"/> when tokens are saved. It does not ask the server, so the session may have ended there.</returns>
	Task<bool> IsSignedInAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Signs in: asks the server for a nonce, lets the user choose a Google account, trades the ID token for a session and saves its tokens.
	/// </summary>
	/// <param name="cancellationToken">Cancels the sign-in.</param>
	/// <returns>A task with success, or the failure of the step that broke.</returns>
	Task<Result> SignInAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads the account of the signed-in user.
	/// </summary>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task with the account, or a failure; <see cref="AccountErrors.NotSignedInCode"/> when there is no session any more.</returns>
	Task<Result<MeResponse>> GetMeAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Signs this device out: ends the session on the server as far as it can be reached, then forgets the tokens and the Google account
	/// state on the phone. The phone is signed out even when the server cannot be reached; the session then ends there when its refresh
	/// token expires.
	/// </summary>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task that completes when the phone is signed out.</returns>
	Task<Result> SignOutAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes the account on the server and, once the server confirmed it, forgets the tokens on the phone.
	/// </summary>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task with success, or a failure; the session is kept when the server did not confirm.</returns>
	Task<Result> DeleteAccountAsync(CancellationToken cancellationToken = default);
	#endregion
}
