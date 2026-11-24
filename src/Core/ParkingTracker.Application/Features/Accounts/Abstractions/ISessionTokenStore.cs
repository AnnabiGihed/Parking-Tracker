namespace ParkingTracker.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the place on the phone where the tokens of the session are kept (ADR-0015). It is the encrypted storage of the
///              phone, never the device database, and no implementation logs a token.
/// </summary>
public interface ISessionTokenStore
{
	#region Methods
	/// <summary>
	/// Reads the saved tokens.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the tokens, or <see langword="null"/> when nobody is signed in or the saved tokens cannot be read.</returns>
	Task<SessionTokens?> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Saves the tokens, replacing the ones saved before.
	/// </summary>
	/// <param name="tokens">The new tokens.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the tokens are saved.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
	Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes the saved tokens; nothing happens when there are none.
	/// </summary>
	/// <param name="cancellationToken">Cancels the delete.</param>
	/// <returns>A task that completes when the tokens are gone.</returns>
	Task ClearAsync(CancellationToken cancellationToken = default);
	#endregion
}
