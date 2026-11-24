using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to Google sign-in on the phone (ADR-0014): asks the user to choose a Google account and returns a Google ID token bound to
///              the nonce of the server. The ID token is a credential: no implementation or caller logs it or keeps it after the exchange.
/// </summary>
public interface IGoogleSignIn
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether this build can sign in with Google, that is, whether it knows the Web client ID of its Google project.
	/// </summary>
	bool IsAvailable { get; }
	#endregion

	#region Methods
	/// <summary>
	/// Shows the Google account chooser and returns the ID token of the chosen account.
	/// </summary>
	/// <param name="nonce">The one-time nonce of the server, which Google puts in the ID token.</param>
	/// <param name="cancellationToken">Cancels the chooser.</param>
	/// <returns>
	/// A task with the ID token, or a failure with the code <see cref="AccountErrors.CancelledCode"/> (the user left),
	/// <see cref="AccountErrors.NoAccountCode"/> (no Google account on the phone) or <see cref="AccountErrors.UnavailableCode"/> (anything else).
	/// </returns>
	Task<Result<string>> SignInAsync(string nonce, CancellationToken cancellationToken = default);

	/// <summary>
	/// Forgets the account the phone remembers for Google sign-in, so that the next sign-in asks again. It never fails: there is nothing to do when it cannot.
	/// </summary>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task that completes when the state is cleared or the attempt was given up.</returns>
	Task ClearStateAsync(CancellationToken cancellationToken = default);
	#endregion
}
