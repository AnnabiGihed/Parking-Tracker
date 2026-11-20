using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParkingTracker.ApiService.Features.Authentication;
using ParkingTracker.ApiService.Features.RateLimiting;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.EndSession;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Authentication.Models;
using Pivot.Framework.Containers.API.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ApiService.Features.Sessions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The session the caller holds (architecture 5.5): the sign-out of this device. It is a controller of its own because
///              <see cref="SessionsController"/> is anonymous as a whole; this one needs a valid access token (the fallback policy of
///              the host) and is limited per account. The account comes from the <c>sub</c> claim and the device from the <c>sid</c>
///              claim, never from the request.
/// </summary>
[Route("api/v{version:apiVersion}/sessions/current")]
[ApiVersion(1.0)]
[EnableRateLimiting(RateLimitSettings.AccountPolicyName)]
[Tags("Sessions")]
public sealed class CurrentSessionController : ApiController
{
	#region Fields
	/// <summary>
	/// The signed-in user of the request.
	/// </summary>
	private readonly ICurrentUser currentUser;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="CurrentSessionController"/> class.
	/// </summary>
	/// <param name="sender">Sends the commands to their handlers.</param>
	/// <param name="currentUser">The signed-in user of the request.</param>
	public CurrentSessionController(ISender sender, ICurrentUser currentUser)
		: base(sender)
	{
		this.currentUser = currentUser;
	}
	#endregion

	#region Endpoints
	/// <summary>
	/// Signs out the device of the access token.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>204 when the device is signed out, also when it was already; 401 without a valid access token that names a session.</returns>
	[HttpDelete]
	[EndpointName("deleteCurrentSession")]
	[EndpointSummary("Sign out this device")]
	[EndpointDescription("Revokes the device session named in the access token (its sid claim): every refresh token of the device stops working, other devices are not affected. Idempotent: a session that is gone or already revoked answers 204 as well. Access tokens already issued stay valid until they expire (15 minutes); only the refresh is cut at once. Limited per account: 120 requests per minute with a burst of 30.")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
	public async Task<IActionResult> Delete(CancellationToken cancellationToken)
	{
		var family = User.FindFirst(AccessTokenIssuer.SessionClaim)?.Value;
		if (currentUser.UserId is not { } accountId || !Guid.TryParse(family, out var familyId) || familyId == Guid.Empty)
		{
			return Unauthorized();
		}

		var result = await Sender.Send(new EndSessionCommand(accountId, familyId), cancellationToken).ConfigureAwait(false);
		return result.IsSuccess ? NoContent() : HandleResult(Result.Failure<bool>(result.Error, result.ResultExceptionType));
	}
	#endregion
}
