using System.Globalization;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParkingTracker.ApiService.Features.RateLimiting;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.SaveProfile;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Backend.Application.Features.Accounts.Queries.GetProfile;
using ParkingTracker.Contracts.Features.Profiles;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Authentication.Models;
using Pivot.Framework.Containers.API.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ApiService.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The profile document of the signed-in user (ADR-0032, architecture 5.5): the read and the conditional write. The revision
///              is the strong <c>ETag</c>; a write names what it is based on with <c>If-Match</c> (the revision) or <c>If-None-Match: *</c>
///              (create), so a phone never overwrites what it has not seen. Pivot maps no status for a failed precondition, so the
///              controller answers 412 and 428 itself, as problem documents like the other answers. A profile that cannot be read is a
///              500 without a cause. Limited per account under the <c>profile</c> policy. The account comes from the <c>sub</c> claim.
/// </summary>
[Route("api/v{version:apiVersion}/me/profile")]
[ApiVersion(1.0)]
[EnableRateLimiting(RateLimitSettings.ProfilePolicyName)]
[Tags("Profile")]
public sealed class ProfileController : ApiController
{
	#region Constants
	/// <summary>
	/// The most bytes the body of a write may have: the 32 KB the document may have in plain text, with room for the JSON escapes
	/// of the request and no more.
	/// </summary>
	public const int MaximumBodyBytes = 40960;

	/// <summary>
	/// The value of <c>If-None-Match</c> that asks to create.
	/// </summary>
	private const string CreateOnlyValue = "*";

	/// <summary>
	/// The prefix of a weak entity tag.
	/// </summary>
	private const string WeakPrefix = "W/";

	/// <summary>
	/// The error code of a write without a precondition header.
	/// </summary>
	private const string ProfileRequiredType = "Profile.PreconditionRequired";
	#endregion

	#region Fields
	/// <summary>
	/// The signed-in user of the request.
	/// </summary>
	private readonly ICurrentUser currentUser;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileController"/> class.
	/// </summary>
	/// <param name="sender">Sends the queries and commands to their handlers.</param>
	/// <param name="currentUser">The signed-in user of the request.</param>
	public ProfileController(ISender sender, ICurrentUser currentUser)
		: base(sender)
	{
		this.currentUser = currentUser;
	}
	#endregion

	#region Endpoints
	/// <summary>
	/// Reads the profile document of the own account.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>200 with the document and its ETag, 304 for a matching If-None-Match, 404 without a profile, 401 without a valid access token.</returns>
	[HttpGet]
	[EndpointName("getProfile")]
	[EndpointSummary("Get the own profile")]
	[EndpointDescription("Returns the profile document of the account (places, vehicle and residents' card), decrypted. The ETag header is the revision in quotes, for example \"3\". An If-None-Match that names the current revision (or *) answers 304 without a body. 404 when the account has no profile yet. A stored profile that cannot be decrypted answers 500 without a cause. Limited per account: 30 requests per minute with a burst of 10.")]
	[ProducesResponseType<ProfileDocument>(StatusCodes.Status200OK, "application/json")]
	[ProducesResponseType(StatusCodes.Status304NotModified)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
	public async Task<IActionResult> Get(CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not { } accountId)
		{
			return Unauthorized();
		}

		var result = await Sender.Send(new GetProfileQuery(accountId), cancellationToken).ConfigureAwait(false);
		if (result.IsFailure)
		{
			return AnswerFailure(result);
		}

		Response.Headers.ETag = FormatEntityTag(result.Value.Revision);
		Response.Headers.CacheControl = "private, no-cache";
		return MatchesAny(Request.Headers.IfNoneMatch.ToString(), result.Value.Revision)
			? StatusCode(StatusCodes.Status304NotModified)
			: Ok(result.Value.Document);
	}

	/// <summary>
	/// Stores the profile document of the own account, if it is based on what is stored.
	/// </summary>
	/// <param name="document">The profile document.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>200 with the new revision and ETag; 400 for an invalid document or header; 401; 412 for a stale write; 413; 428 without a precondition.</returns>
	[HttpPut]
	[RequestSizeLimit(MaximumBodyBytes)]
	[EndpointName("putProfile")]
	[EndpointSummary("Store the own profile")]
	[EndpointDescription("Validates and stores the profile document, encrypted. The write must say what it is based on: If-Match with the revision in quotes (for example \"3\") to replace that revision, or If-None-Match: * to create the first profile; neither is 428, a malformed value or both together is 400, and a revision that is not the stored one, a create over an existing profile or a write that lost a race is 412 (read the profile again, merge, write again). The document has at most 32 KB of plain text and the body at most 40 KB (413 above), the schema version is 1, coordinates lie in the Brussels region, at most 50 favourite places. The answer carries the new revision in the body and in the ETag header. Limited per account: 30 requests per minute with a burst of 10.")]
	[ProducesResponseType<ProfileRevisionResponse>(StatusCodes.Status200OK, "application/json")]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired, "application/problem+json")]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
	public async Task<IActionResult> Put([FromBody] ProfileDocument document, CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not { } accountId)
		{
			return Unauthorized();
		}

		var refusal = ReadCondition(out var condition);
		if (refusal is not null)
		{
			return refusal;
		}

		var result = await Sender.Send(new SaveProfileCommand(accountId, document, condition!), cancellationToken).ConfigureAwait(false);
		if (result.IsFailure)
		{
			return AnswerFailure(result);
		}

		Response.Headers.ETag = FormatEntityTag(result.Value.Revision);
		return Ok(new ProfileRevisionResponse(result.Value.Revision));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads what the write is based on from the request headers.
	/// </summary>
	/// <param name="condition">The condition; <see langword="null"/> when the answer is a refusal.</param>
	/// <returns>The answer to send when the headers are missing (428) or malformed (400); <see langword="null"/> when the condition was read.</returns>
	private ObjectResult? ReadCondition(out ProfileWriteCondition? condition)
	{
		condition = null;
		var ifMatch = Request.Headers.IfMatch;
		var ifNoneMatch = Request.Headers.IfNoneMatch;
		if (ifMatch.Count == 0 && ifNoneMatch.Count == 0)
		{
			return Refuse(StatusCodes.Status428PreconditionRequired, "Precondition Required", ProfileRequiredType, "Send If-Match with the revision, or If-None-Match: * to create.");
		}

		if (ifMatch.Count > 0 && ifNoneMatch.Count > 0)
		{
			return BadPrecondition();
		}

		if (ifNoneMatch.Count > 0)
		{
			if (ifNoneMatch.Count == 1 && string.Equals(ifNoneMatch[0], CreateOnlyValue, StringComparison.Ordinal))
			{
				condition = ProfileWriteCondition.CreateOnly;
				return null;
			}

			return BadPrecondition();
		}

		if (ifMatch.Count == 1 && TryParseEntityTag(ifMatch[0], out var revision))
		{
			condition = ProfileWriteCondition.ForRevision(revision);
			return null;
		}

		return BadPrecondition();
	}

	/// <summary>
	/// Answers a failed result: 412 for a failed precondition, 500 without a cause for a profile that cannot be read, the usual mapping otherwise.
	/// </summary>
	/// <param name="result">The failed result.</param>
	/// <returns>The answer.</returns>
	private ActionResult AnswerFailure(Result result)
	{
		var code = result.Error.Code;
		if (string.Equals(code, ProfileErrors.PreconditionFailed.Code, StringComparison.Ordinal))
		{
			return Refuse(StatusCodes.Status412PreconditionFailed, "Precondition Failed", code, "Read the profile again before writing.");
		}

		if (string.Equals(code, ProfileErrors.Unreadable.Code, StringComparison.Ordinal) || string.Equals(code, ProfileErrors.KeyUnknown.Code, StringComparison.Ordinal))
		{
			// One answer for every cause: the cause is in the log, and the key being unknown must not be told to a client.
			return Refuse(StatusCodes.Status500InternalServerError, "Internal Server Error", ProfileErrors.Unreadable.Code, null);
		}

		return HandleFailure(result);
	}

	/// <summary>
	/// Answers 400 for a precondition header that is malformed or contradicts the other.
	/// </summary>
	/// <returns>The answer.</returns>
	private ObjectResult BadPrecondition()
	{
		return Refuse(StatusCodes.Status400BadRequest, "Bad Request", "Profile.PreconditionInvalid", "Send either If-Match with one revision in quotes, or If-None-Match: *.");
	}

	/// <summary>
	/// Builds a problem answer.
	/// </summary>
	/// <param name="status">The status code.</param>
	/// <param name="title">The title.</param>
	/// <param name="type">The error code, as the other problems carry it.</param>
	/// <param name="detail">What to do, without private data; <see langword="null"/> for none.</param>
	/// <returns>The answer.</returns>
	private ObjectResult Refuse(int status, string title, string type, string? detail)
	{
		return StatusCode(status, new ProblemDetails { Status = status, Title = title, Type = type, Detail = detail });
	}

	/// <summary>
	/// Writes a revision as a strong entity tag.
	/// </summary>
	/// <param name="revision">The revision.</param>
	/// <returns>The revision in quotes.</returns>
	private static string FormatEntityTag(long revision)
	{
		return $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";
	}

	/// <summary>
	/// Reads a strong entity tag that holds a revision.
	/// </summary>
	/// <param name="value">The header value.</param>
	/// <param name="revision">The revision.</param>
	/// <returns><see langword="true"/> when the value is a revision of 1 or more in quotes, written without leading zeros or signs.</returns>
	private static bool TryParseEntityTag(string? value, out long revision)
	{
		revision = 0;
		if (value is not { Length: >= 3 } || value[0] != '"' || value[^1] != '"')
		{
			return false;
		}

		var digits = value[1..^1];
		return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out revision)
			&& revision >= 1
			&& string.Equals(digits, revision.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	/// <summary>
	/// Tells whether an If-None-Match header names the current revision, with the weak comparison that GET uses.
	/// </summary>
	/// <param name="header">The header value; empty for none.</param>
	/// <param name="revision">The current revision.</param>
	/// <returns><see langword="true"/> when one of the listed tags is the current one, or the list is <c>*</c>.</returns>
	private static bool MatchesAny(string header, long revision)
	{
		var current = FormatEntityTag(revision);
		foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			var tag = part.StartsWith(WeakPrefix, StringComparison.Ordinal) ? part[WeakPrefix.Length..] : part;
			if (string.Equals(tag, CreateOnlyValue, StringComparison.Ordinal) || string.Equals(tag, current, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
	#endregion
}
