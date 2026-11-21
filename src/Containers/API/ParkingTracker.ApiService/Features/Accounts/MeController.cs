using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParkingTracker.ApiService.Features.RateLimiting;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.EraseAccount;
using ParkingTracker.Backend.Application.Features.Accounts.Queries.GetMe;
using ParkingTracker.Contracts.Features.Accounts;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Authentication.Models;
using Pivot.Framework.Containers.API.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ApiService.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The own account of the signed-in user (architecture 5.5): the read and the erasure. It needs a valid access token
///              (the fallback policy of the host) and is limited per account. The account comes from the <c>sub</c> claim, never from
///              the request.
/// </summary>
[Route("api/v{version:apiVersion}/me")]
[ApiVersion(1.0)]
[EnableRateLimiting(RateLimitSettings.AccountPolicyName)]
[Tags("Account")]
public sealed class MeController : ApiController
{
	#region Fields
	/// <summary>
	/// The signed-in user of the request.
	/// </summary>
	private readonly ICurrentUser currentUser;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MeController"/> class.
	/// </summary>
	/// <param name="sender">Sends the queries to their handlers.</param>
	/// <param name="currentUser">The signed-in user of the request.</param>
	public MeController(ISender sender, ICurrentUser currentUser)
		: base(sender)
	{
		this.currentUser = currentUser;
	}
	#endregion

	#region Endpoints
	/// <summary>
	/// Reads the own account.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>200 with the account, or 401 without a valid access token or when the account no longer exists.</returns>
	[HttpGet]
	[EndpointName("getMe")]
	[EndpointSummary("Get the own account")]
	[EndpointDescription("Returns the account of the access token: its identifier, the day it was created, the last day the user signed in or refreshed, and the number of devices signed in. Nothing in the answer identifies the person. Limited per account: 120 requests per minute with a burst of 30.")]
	[ProducesResponseType<MeResponse>(StatusCodes.Status200OK, "application/json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
	public async Task<IActionResult> Get(CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not { } accountId)
		{
			return Unauthorized();
		}

		var result = await Sender.Send(new GetMeQuery(accountId), cancellationToken).ConfigureAwait(false);
		return HandleResult(result.Map(account => new MeResponse(account.AccountId, account.CreatedOn, account.LastSeenOn, account.ActiveSessionCount)));
	}

	/// <summary>
	/// Erases the own account: the account, all its device sessions and the link to the Google subject.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>204 when the account is gone, also when it was gone already; 401 without a valid access token.</returns>
	[HttpDelete]
	[EndpointName("deleteMe")]
	[EndpointSummary("Delete the own account")]
	[EndpointDescription("Deletes the account and every device session in one transaction and records the hash of the Google subject in the erasure log, which only serves to apply the erasure again after a backup restore. It is no ban: the same Google account can sign in again and gets a new account. Idempotent: a repeated call with an access token that has not expired answers 204 as well. Access tokens already issued stay valid until they expire, but they name an account that no longer exists, so GET /me then answers 401. Limited per account: 120 requests per minute with a burst of 30.")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
	public async Task<IActionResult> Delete(CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not { } accountId)
		{
			return Unauthorized();
		}

		var result = await Sender.Send(new EraseAccountCommand(accountId), cancellationToken).ConfigureAwait(false);
		return result.IsSuccess ? NoContent() : HandleResult(Result.Failure<bool>(result.Error, result.ResultExceptionType));
	}
	#endregion
}
