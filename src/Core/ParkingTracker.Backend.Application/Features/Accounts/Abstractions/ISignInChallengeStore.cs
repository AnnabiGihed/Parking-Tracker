namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the nonces the API issued for sign-ins. A nonce is kept in the database, so a deployment during a sign-in
///              loses none, and it is consumed with one atomic statement, so two requests cannot both use it. Both operations
///              take effect at once, not with a unit of work: a consumed nonce stays consumed whatever happens afterwards.
/// </summary>
public interface ISignInChallengeStore
{
	#region Methods
	/// <summary>
	/// Stores a new nonce and removes the nonces that expired before <paramref name="now"/>.
	/// </summary>
	/// <param name="nonce">The nonce.</param>
	/// <param name="expiresAt">The instant after which the nonce is refused.</param>
	/// <param name="now">The current moment, the limit of the purge.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the nonce is stored.</returns>
	Task CreateAsync(string nonce, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default);

	/// <summary>
	/// Consumes a nonce: deletes it when it exists and has not expired at <paramref name="now"/>.
	/// </summary>
	/// <param name="nonce">The nonce.</param>
	/// <param name="now">The current moment.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task with <see langword="true"/> when this call consumed the nonce; <see langword="false"/> when it is unknown, used or expired.</returns>
	Task<bool> ConsumeAsync(string nonce, DateTimeOffset now, CancellationToken cancellationToken = default);
	#endregion
}
