using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Contracts.Features.Sessions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The three calls of the API that need no bearer token: the sign-in challenge, the exchange of a Google ID token and the
///              refresh of a session. It turns every outcome into a <see cref="Result"/> with the codes of <see cref="AccountErrors"/>:
///              401 is <see cref="AccountErrors.RefusedCode"/> (the only answer that ends a session), a connection that fails or times out is
///              <see cref="AccountErrors.NetworkCode"/>, and anything else is <see cref="AccountErrors.ServerCode"/>. The refresh is never retried
///              here: the refresh token is single-use, so a blind retry could burn it.
/// </summary>
internal sealed class SessionApiClient
{
	#region Constants
	/// <summary>
	/// The name of the HTTP client that has no authentication handler.
	/// </summary>
	public const string ClientName = "session-api";
	#endregion

	#region Fields
	/// <summary>
	/// The HTTP client to the API.
	/// </summary>
	private readonly HttpClient http;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SessionApiClient"/> class.
	/// </summary>
	/// <param name="http">The HTTP client to the API, with its base address set.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="http"/> is <see langword="null"/>.</exception>
	public SessionApiClient(HttpClient http)
	{
		ArgumentNullException.ThrowIfNull(http);
		this.http = http;
	}
	#endregion

	#region Internal Methods
	/// <summary>
	/// Asks for a one-time nonce to put in the Google sign-in.
	/// </summary>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task with the challenge, or a failure.</returns>
	internal async Task<Result<SignInChallengeResponse>> GetChallengeAsync(CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/sessions/challenges");
		return await SendAsync(request, AccountJsonContext.Default.SignInChallengeResponse, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Trades a Google ID token for a session.
	/// </summary>
	/// <param name="idToken">The ID token, which is sent once and never kept.</param>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task with the session, or a failure.</returns>
	internal async Task<Result<SessionResponse>> ExchangeAsync(string idToken, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/sessions")
		{
			Content = JsonContent.Create(new CreateSessionRequest(idToken), AccountJsonContext.Default.CreateSessionRequest),
		};
		return await SendAsync(request, AccountJsonContext.Default.SessionResponse, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Trades a refresh token for new tokens.
	/// </summary>
	/// <param name="refreshToken">The refresh token, which the API accepts once.</param>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task with the new session, or a failure; <see cref="AccountErrors.RefusedCode"/> means the API rejected the token.</returns>
	internal async Task<Result<SessionResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/sessions/refresh")
		{
			Content = JsonContent.Create(new RefreshSessionRequest(refreshToken), AccountJsonContext.Default.RefreshSessionRequest),
		};
		return await SendAsync(request, AccountJsonContext.Default.SessionResponse, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Builds the failure for an HTTP status that is not a success.
	/// </summary>
	/// <param name="status">The status the API answered.</param>
	/// <returns>The refused failure for 401, the server failure for anything else.</returns>
	internal static Error ErrorFor(HttpStatusCode status) => status == HttpStatusCode.Unauthorized ? AccountErrors.Refused() : AccountErrors.Server();
	#endregion

	#region Private Helpers
	/// <summary>
	/// Sends a request and reads the JSON answer.
	/// </summary>
	/// <typeparam name="T">The type of the answer.</typeparam>
	/// <param name="request">The request.</param>
	/// <param name="typeInfo">The generated metadata of <typeparamref name="T"/>.</param>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task with the answer, or a failure.</returns>
	private async Task<Result<T>> SendAsync<T>(HttpRequestMessage request, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
		where T : class
	{
		try
		{
			using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return Result.Failure<T>(ErrorFor(response.StatusCode));
			}

			var value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false);
			return value is null ? Result.Failure<T>(AccountErrors.Server()) : Result.Success(value);
		}
		catch (HttpRequestException)
		{
			return Result.Failure<T>(AccountErrors.Network());
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// The time limit of the client ran out; a cancel by the caller is not caught.
			return Result.Failure<T>(AccountErrors.Network());
		}
		catch (JsonException)
		{
			return Result.Failure<T>(AccountErrors.Server());
		}
	}
	#endregion
}
