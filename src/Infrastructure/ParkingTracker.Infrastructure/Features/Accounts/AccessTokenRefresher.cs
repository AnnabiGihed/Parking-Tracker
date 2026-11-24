using ParkingTracker.Application.Features.Accounts.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The one place that refreshes the session (ADR-0015). Callers that need a token while a refresh runs wait for it and share its
///              result, so the single-use refresh token is spent once. The access token is renewed shortly before it expires, or when the API
///              refused it. A refresh that the API rejects (401) deletes the tokens, which is the only way the app signs out by itself; a
///              network error or a server error keeps the tokens, and an access token that is still valid is used meanwhile.
/// </summary>
internal sealed class AccessTokenRefresher : IDisposable
{
	#region Static Instances
	/// <summary>
	/// How long before its expiry an access token counts as expired, so that a request does not arrive after it ended.
	/// </summary>
	internal static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);
	#endregion

	#region Fields
	/// <summary>
	/// Lets one refresh run at a time.
	/// </summary>
	private readonly SemaphoreSlim gate = new(1, 1);

	/// <summary>
	/// Creates the HTTP clients to the API.
	/// </summary>
	private readonly IHttpClientFactory clients;

	/// <summary>
	/// Where the tokens are kept.
	/// </summary>
	private readonly ISessionTokenStore store;

	/// <summary>
	/// The clock that tells when an access token expires.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccessTokenRefresher"/> class.
	/// </summary>
	/// <param name="clients">Creates the HTTP clients to the API.</param>
	/// <param name="store">Where the tokens are kept.</param>
	/// <param name="clock">The clock that tells when an access token expires.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public AccessTokenRefresher(IHttpClientFactory clients, ISessionTokenStore store, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(clients);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(clock);
		this.clients = clients;
		this.store = store;
		this.clock = clock;
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose() => gate.Dispose();
	#endregion

	#region Internal Methods
	/// <summary>
	/// Gets an access token for a request, refreshing it first when it is about to expire.
	/// </summary>
	/// <param name="cancellationToken">Cancels the wait and the refresh.</param>
	/// <returns>A task with the token, or a failure: <see cref="AccountErrors.NotSignedInCode"/> when there is no session, a network or server failure when the refresh could not be made and the token had already expired.</returns>
	internal Task<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken) => EnsureAsync(rejectedAccessToken: null, cancellationToken);

	/// <summary>
	/// Gets a new access token after the API refused one. When another caller already replaced it, that one is returned and no second refresh is made.
	/// </summary>
	/// <param name="rejectedAccessToken">The access token the API refused.</param>
	/// <param name="cancellationToken">Cancels the wait and the refresh.</param>
	/// <returns>A task with the token, or a failure as for <see cref="GetAccessTokenAsync"/>.</returns>
	internal Task<Result<string>> RefreshAfterRejectionAsync(string rejectedAccessToken, CancellationToken cancellationToken) => EnsureAsync(rejectedAccessToken, cancellationToken);
	#endregion

	#region Private Helpers
	/// <summary>
	/// Returns a usable access token, refreshing under the gate when there is none.
	/// </summary>
	/// <param name="rejectedAccessToken">The token the API refused, or <see langword="null"/> when none was.</param>
	/// <param name="cancellationToken">Cancels the wait and the refresh.</param>
	/// <returns>A task with the token, or a failure.</returns>
	private async Task<Result<string>> EnsureAsync(string? rejectedAccessToken, CancellationToken cancellationToken)
	{
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var tokens = await store.GetAsync(cancellationToken).ConfigureAwait(false);
			if (tokens is null)
			{
				return Result.Failure<string>(AccountErrors.NotSignedIn());
			}

			var now = clock.GetUtcNow();
			if (IsUsable(tokens, rejectedAccessToken, now))
			{
				return Result.Success(tokens.AccessToken);
			}

			return await RefreshAsync(tokens, rejectedAccessToken, now, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// Tells whether the saved access token can be sent as it is: not the refused one, and not about to expire.
	/// </summary>
	/// <param name="tokens">The saved tokens.</param>
	/// <param name="rejectedAccessToken">The token the API refused, or <see langword="null"/>.</param>
	/// <param name="now">The current moment.</param>
	/// <returns><see langword="true"/> when no refresh is needed.</returns>
	private static bool IsUsable(SessionTokens tokens, string? rejectedAccessToken, DateTimeOffset now)
	{
		var isRefused = string.Equals(tokens.AccessToken, rejectedAccessToken, StringComparison.Ordinal);
		return !isRefused && tokens.AccessTokenExpiresAt - now > ExpirySkew;
	}

	/// <summary>
	/// Trades the refresh token for new tokens and saves them; deletes the tokens when the API rejects the refresh token.
	/// </summary>
	/// <param name="tokens">The saved tokens.</param>
	/// <param name="rejectedAccessToken">The token the API refused, or <see langword="null"/>.</param>
	/// <param name="now">The current moment.</param>
	/// <param name="cancellationToken">Cancels the refresh.</param>
	/// <returns>A task with the new access token, or a failure.</returns>
	private async Task<Result<string>> RefreshAsync(SessionTokens tokens, string? rejectedAccessToken, DateTimeOffset now, CancellationToken cancellationToken)
	{
		var api = new SessionApiClient(clients.CreateClient(SessionApiClient.ClientName));
		var refreshed = await api.RefreshAsync(tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
		if (refreshed.IsSuccess)
		{
			var value = refreshed.Value;
			await store.SaveAsync(new SessionTokens(value.AccessToken, value.AccessTokenExpiresAt, value.RefreshToken), cancellationToken).ConfigureAwait(false);
			return Result.Success(value.AccessToken);
		}

		if (string.Equals(refreshed.Error.Code, AccountErrors.RefusedCode, StringComparison.Ordinal))
		{
			await store.ClearAsync(cancellationToken).ConfigureAwait(false);
			return Result.Failure<string>(AccountErrors.NotSignedIn());
		}

		// Not rejected, so the session stays: an access token that has not ended yet is still good for this request.
		var canUseCurrent = rejectedAccessToken is null && tokens.AccessTokenExpiresAt > now;
		return canUseCurrent ? Result.Success(tokens.AccessToken) : Result.Failure<string>(refreshed.Error);
	}
	#endregion
}
