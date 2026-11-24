using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Contracts.Features.Accounts;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ISessionClient"/> over the API (ADR-0014, ADR-0015). Sign-in runs the challenge, the Google account chooser and
///              the exchange through the client without a bearer and saves the tokens; the other calls go through the client whose
///              <see cref="AuthenticatedHandler"/> adds the bearer and refreshes it. The tokens and the ID token are never logged or put in a message.
/// </summary>
internal sealed class SessionClient : ISessionClient
{
	#region Fields
	/// <summary>
	/// The HTTP client of the signed-in user, with the authentication handler.
	/// </summary>
	private readonly HttpClient authenticated;

	/// <summary>
	/// Creates the HTTP clients that have no authentication handler.
	/// </summary>
	private readonly IHttpClientFactory clients;

	/// <summary>
	/// Where the tokens are kept.
	/// </summary>
	private readonly ISessionTokenStore store;

	/// <summary>
	/// Google sign-in on the phone.
	/// </summary>
	private readonly IGoogleSignIn google;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SessionClient"/> class.
	/// </summary>
	/// <param name="authenticated">The HTTP client of the signed-in user, with the authentication handler.</param>
	/// <param name="clients">Creates the HTTP clients that have no authentication handler.</param>
	/// <param name="store">Where the tokens are kept.</param>
	/// <param name="google">Google sign-in on the phone.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public SessionClient(HttpClient authenticated, IHttpClientFactory clients, ISessionTokenStore store, IGoogleSignIn google)
	{
		ArgumentNullException.ThrowIfNull(authenticated);
		ArgumentNullException.ThrowIfNull(clients);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(google);
		this.authenticated = authenticated;
		this.clients = clients;
		this.store = store;
		this.google = google;
	}
	#endregion

	#region ISessionClient Implementation
	/// <inheritdoc />
	public async Task<bool> IsSignedInAsync(CancellationToken cancellationToken = default)
	{
		return await store.GetAsync(cancellationToken).ConfigureAwait(false) is not null;
	}

	/// <inheritdoc />
	public async Task<Result> SignInAsync(CancellationToken cancellationToken = default)
	{
		if (!google.IsAvailable)
		{
			return Result.Failure(AccountErrors.Unavailable());
		}

		var api = new SessionApiClient(clients.CreateClient(SessionApiClient.ClientName));
		var challenge = await api.GetChallengeAsync(cancellationToken).ConfigureAwait(false);
		if (challenge.IsFailure)
		{
			return Result.Failure(challenge.Error);
		}

		var idToken = await google.SignInAsync(challenge.Value.Nonce, cancellationToken).ConfigureAwait(false);
		if (idToken.IsFailure)
		{
			return Result.Failure(idToken.Error);
		}

		var session = await api.ExchangeAsync(idToken.Value, cancellationToken).ConfigureAwait(false);
		if (session.IsFailure)
		{
			return Result.Failure(session.Error);
		}

		var value = session.Value;
		await store.SaveAsync(new SessionTokens(value.AccessToken, value.AccessTokenExpiresAt, value.RefreshToken), cancellationToken).ConfigureAwait(false);
		return Result.Success();
	}

	/// <inheritdoc />
	public async Task<Result<MeResponse>> GetMeAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			using var response = await authenticated.GetAsync("api/v1/me", cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return Result.Failure<MeResponse>(ErrorFor(response.StatusCode));
			}

			var me = await response.Content.ReadFromJsonAsync(AccountJsonContext.Default.MeResponse, cancellationToken).ConfigureAwait(false);
			return me is null ? Result.Failure<MeResponse>(AccountErrors.Server()) : Result.Success(me);
		}
		catch (HttpRequestException exception)
		{
			return Result.Failure<MeResponse>(ErrorOf(exception));
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return Result.Failure<MeResponse>(AccountErrors.Network());
		}
		catch (JsonException)
		{
			return Result.Failure<MeResponse>(AccountErrors.Server());
		}
	}

	/// <inheritdoc />
	public async Task<Result> SignOutAsync(CancellationToken cancellationToken = default)
	{
		// The outcome is not used: the phone signs out whether or not the server could be told.
		_ = await DeleteAsync("api/v1/sessions/current", cancellationToken).ConfigureAwait(false);
		await ForgetSessionAsync(cancellationToken).ConfigureAwait(false);
		return Result.Success();
	}

	/// <inheritdoc />
	public async Task<Result> DeleteAccountAsync(CancellationToken cancellationToken = default)
	{
		var deleted = await DeleteAsync("api/v1/me", cancellationToken).ConfigureAwait(false);
		if (deleted.IsSuccess)
		{
			await ForgetSessionAsync(cancellationToken).ConfigureAwait(false);
		}

		return deleted;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Sends an authenticated DELETE.
	/// </summary>
	/// <param name="path">The path below the base address.</param>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task with success for a 2xx answer, or the failure.</returns>
	private async Task<Result> DeleteAsync(string path, CancellationToken cancellationToken)
	{
		try
		{
			using var response = await authenticated.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
			return response.IsSuccessStatusCode ? Result.Success() : Result.Failure(ErrorFor(response.StatusCode));
		}
		catch (HttpRequestException exception)
		{
			return Result.Failure(ErrorOf(exception));
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return Result.Failure(AccountErrors.Network());
		}
	}

	/// <summary>
	/// Forgets the tokens and the Google account state of the phone.
	/// </summary>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task that completes when both are cleared.</returns>
	private async Task ForgetSessionAsync(CancellationToken cancellationToken)
	{
		await store.ClearAsync(cancellationToken).ConfigureAwait(false);
		await google.ClearStateAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Builds the failure for a request that did not complete: the reason of a refresh that failed, or a network failure.
	/// </summary>
	/// <param name="exception">The exception of the request.</param>
	/// <returns>The failure.</returns>
	private static Error ErrorOf(HttpRequestException exception) => exception is TokenRefreshException refresh ? refresh.Error : AccountErrors.Network();

	/// <summary>
	/// Builds the failure for a status that is not a success: 401 on an authenticated call means there is no session left.
	/// </summary>
	/// <param name="status">The status the API answered.</param>
	/// <returns>The failure.</returns>
	private static Error ErrorFor(HttpStatusCode status) => status == HttpStatusCode.Unauthorized ? AccountErrors.NotSignedIn() : AccountErrors.Server();
	#endregion
}
