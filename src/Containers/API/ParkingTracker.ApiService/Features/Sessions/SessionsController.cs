using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParkingTracker.ApiService.Features.RateLimiting;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.CreateSignInChallenge;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.ExchangeGoogleIdToken;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.RefreshSession;
using ParkingTracker.Contracts.Features.Sessions;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Containers.API.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ApiService.Features.Sessions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The sign-in endpoints of the API (ADR-0014, architecture 5.5): the challenge that issues a one-time nonce and the
///              exchange of a Google ID token for the tokens of a new session, and the refresh of the tokens of a session. All are
///              anonymous, limited per client address (the refresh under a policy of its own), and run outside any transaction
///              middleware: the exchange and the refresh commit through the unit of work themselves, so a consumed nonce and the
///              revocation of a family after a reuse stay when the answer is a refusal.
/// </summary>
[Route("api/v{version:apiVersion}/sessions")]
[ApiVersion(1.0)]
[AllowAnonymous]
[EnableRateLimiting(RateLimitSettings.SessionPolicyName)]
[Tags("Sessions")]
public sealed class SessionsController : ApiController
{
	#region Constants
	/// <summary>
	/// The most bytes the body of a sign-in may have: twice the longest ID token the handler reads, with the JSON around it.
	/// </summary>
	public const int MaximumBodyBytes = 16384;

	/// <summary>
	/// The most bytes the body of a refresh may have: a refresh token has 43 characters, so this leaves room for the JSON and no more.
	/// </summary>
	public const int MaximumRefreshBodyBytes = 1024;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SessionsController"/> class.
	/// </summary>
	/// <param name="sender">Sends the commands to their handlers.</param>
	public SessionsController(ISender sender)
		: base(sender)
	{
	}
	#endregion

	#region Endpoints
	/// <summary>
	/// Issues a one-time nonce for a sign-in.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>200 with the nonce and its expiry.</returns>
	[HttpPost("challenges")]
	[EndpointName("createSignInChallenge")]
	[EndpointSummary("Create a sign-in challenge")]
	[EndpointDescription("Issues a random single-use nonce, valid for 5 minutes. The app passes it to Google (Android Credential Manager) so that the ID token it gets back is bound to this challenge. Expired nonces are purged on every call. Limited to 10 session requests per minute per client address.")]
	[ProducesResponseType<SignInChallengeResponse>(StatusCodes.Status200OK, "application/json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
	public async Task<IActionResult> CreateChallenge(CancellationToken cancellationToken)
	{
		var result = await Sender.Send(new CreateSignInChallengeCommand(), cancellationToken).ConfigureAwait(false);
		return HandleResult(result.Map(challenge => new SignInChallengeResponse(challenge.Nonce, challenge.ExpiresAt)));
	}

	/// <summary>
	/// Exchanges a Google ID token for the tokens of a new session.
	/// </summary>
	/// <param name="request">The body with the Google ID token.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>200 with the tokens, or 401 for every refusal.</returns>
	[HttpPost]
	[RequestSizeLimit(MaximumBodyBytes)]
	[EndpointName("createSession")]
	[EndpointSummary("Sign in with a Google ID token")]
	[EndpointDescription("Validates the Google ID token (signature, issuer, audience, expiry), consumes the single-use nonce it carries, finds or creates the account of the Google subject and starts a device session. Returns the API access token (ES256 JWT, 15 minutes) and a single-use refresh token. Every refusal is the same 401 answer without a cause; the cause is only logged. A consumed nonce stays consumed even when the answer is a refusal. Limited to 10 session requests per minute per client address.")]
	[ProducesResponseType<SessionResponse>(StatusCodes.Status200OK, "application/json")]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
	public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest request, CancellationToken cancellationToken)
	{
		var result = await Sender.Send(new ExchangeGoogleIdTokenCommand(request.IdToken), cancellationToken).ConfigureAwait(false);
		return HandleResult(result.Map(tokens => new SessionResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken)));
	}

	/// <summary>
	/// Exchanges a refresh token for a new access token and the next refresh token.
	/// </summary>
	/// <param name="request">The body with the refresh token.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>200 with the new tokens, or 401 for every refusal.</returns>
	[HttpPost("refresh")]
	[RequestSizeLimit(MaximumRefreshBodyBytes)]
	[EnableRateLimiting(RateLimitSettings.RefreshPolicyName)]
	[EndpointName("refreshSession")]
	[EndpointSummary("Refresh a session")]
	[EndpointDescription("Exchanges the single-use refresh token for a new access token (15 minutes) and the next refresh token; the presented token is spent. Presenting a token that was already exchanged revokes the whole family of the device, and the device must sign in again. An unknown, revoked or expired token (30 days unused, 90 days in all) is refused. Every refusal is the same 401 answer without a cause; the cause is only logged. Limited to 30 refresh requests per minute per client address, apart from the sign-in limit.")]
	[ProducesResponseType<SessionResponse>(StatusCodes.Status200OK, "application/json")]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
	public async Task<IActionResult> Refresh([FromBody] RefreshSessionRequest request, CancellationToken cancellationToken)
	{
		var result = await Sender.Send(new RefreshSessionCommand(request.RefreshToken), cancellationToken).ConfigureAwait(false);
		return HandleResult(result.Map(tokens => new SessionResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken)));
	}
	#endregion
}
