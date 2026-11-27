using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Infrastructure.Features.Accounts;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The client of the profile endpoints (ADR-0032) over the HTTP client of the signed-in user, which refreshes the access
///              token and retries once on a 401. A read sends <c>If-None-Match</c> with the revision the phone holds; a write sends
///              <c>If-Match</c> with the revision it replaces, or <c>If-None-Match: *</c> to create. The answers 304, 404, 412 and
///              400, 413 and 428 are outcomes; a network error or timeout, a session that is gone and any other status are failed
///              results. The document is never written to a log or to a failure message.
/// </summary>
internal sealed class ProfileClient : IProfileClient
{
	#region Constants
	/// <summary>
	/// The route of the profile.
	/// </summary>
	private const string Route = "api/v1/me/profile";
	#endregion

	#region Fields
	/// <summary>
	/// The HTTP client of the signed-in user.
	/// </summary>
	private readonly HttpClient http;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileClient"/> class.
	/// </summary>
	/// <param name="http">The HTTP client of the signed-in user, with the authentication handler.</param>
	/// <exception cref="ArgumentNullException"><paramref name="http"/> is <see langword="null"/>.</exception>
	public ProfileClient(HttpClient http)
	{
		ArgumentNullException.ThrowIfNull(http);
		this.http = http;
	}
	#endregion

	#region IProfileClient Implementation
	/// <inheritdoc />
	public async Task<Result<ProfileGetResult>> GetAsync(long? ifNoneMatchRevision, CancellationToken cancellationToken = default)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, Route);
		if (ifNoneMatchRevision is { } revision)
		{
			request.Headers.IfNoneMatch.Add(Tag(revision));
		}

		try
		{
			using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
			return await ReadGetAsync(response, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException exception)
		{
			return Result.Failure<ProfileGetResult>(ErrorOf(exception));
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// The time limit of the client ran out; a cancel by the caller is not caught.
			return Result.Failure<ProfileGetResult>(AccountErrors.Network());
		}
		catch (JsonException)
		{
			return Result.Failure<ProfileGetResult>(AccountErrors.Server());
		}
	}

	/// <inheritdoc />
	public async Task<Result<ProfilePutResult>> PutAsync(ProfileDocument document, long? ifMatchRevision, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(document);
		using var request = new HttpRequestMessage(HttpMethod.Put, Route)
		{
			Content = JsonContent.Create(document, ProfileJsonContext.Default.ProfileDocument),
		};
		if (ifMatchRevision is { } revision)
		{
			request.Headers.IfMatch.Add(Tag(revision));
		}
		else
		{
			request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
		}

		try
		{
			using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
			return await ReadPutAsync(response, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException exception)
		{
			return Result.Failure<ProfilePutResult>(ErrorOf(exception));
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return Result.Failure<ProfilePutResult>(AccountErrors.Network());
		}
		catch (JsonException)
		{
			return Result.Failure<ProfilePutResult>(AccountErrors.Server());
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Interprets the answer of a read.
	/// </summary>
	/// <param name="response">The answer.</param>
	/// <param name="cancellationToken">Cancels the read of the body.</param>
	/// <returns>A task with the outcome, or the failure.</returns>
	private static async Task<Result<ProfileGetResult>> ReadGetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		switch (response.StatusCode)
		{
			case HttpStatusCode.NotModified:
				return ProfileGetResult.NotModified;
			case HttpStatusCode.NotFound:
				return ProfileGetResult.NotFound;
			case HttpStatusCode.Unauthorized:
				return Result.Failure<ProfileGetResult>(AccountErrors.NotSignedIn());
			case HttpStatusCode.OK:
				var document = await response.Content.ReadFromJsonAsync(ProfileJsonContext.Default.ProfileDocument, cancellationToken).ConfigureAwait(false);
				var revision = ReadRevision(response.Headers.ETag);
				return document is null || revision is null
					? Result.Failure<ProfileGetResult>(AccountErrors.Server())
					: ProfileGetResult.Found(revision.Value, document);
			default:
				return Result.Failure<ProfileGetResult>(AccountErrors.Server());
		}
	}

	/// <summary>
	/// Interprets the answer of a write.
	/// </summary>
	/// <param name="response">The answer.</param>
	/// <param name="cancellationToken">Cancels the read of the body.</param>
	/// <returns>A task with the outcome, or the failure.</returns>
	private static async Task<Result<ProfilePutResult>> ReadPutAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		switch (response.StatusCode)
		{
			case HttpStatusCode.OK:
				var body = await response.Content.ReadFromJsonAsync(ProfileJsonContext.Default.ProfileRevisionResponse, cancellationToken).ConfigureAwait(false);
				return body is null ? Result.Failure<ProfilePutResult>(AccountErrors.Server()) : ProfilePutResult.Saved(body.Revision);
			case HttpStatusCode.PreconditionFailed:
				return ProfilePutResult.Stale;
			case HttpStatusCode.BadRequest:
			case HttpStatusCode.RequestEntityTooLarge:
			case HttpStatusCode.PreconditionRequired:
				return ProfilePutResult.Rejected((int)response.StatusCode);
			case HttpStatusCode.Unauthorized:
				return Result.Failure<ProfilePutResult>(AccountErrors.NotSignedIn());
			default:
				return Result.Failure<ProfilePutResult>(AccountErrors.Server());
		}
	}

	/// <summary>
	/// Builds the strong entity tag of a revision, the revision in quotes.
	/// </summary>
	/// <param name="revision">The revision.</param>
	/// <returns>The tag.</returns>
	private static EntityTagHeaderValue Tag(long revision) => new($"\"{revision.ToString(CultureInfo.InvariantCulture)}\"");

	/// <summary>
	/// Reads the revision from the <c>ETag</c> header of an answer.
	/// </summary>
	/// <param name="tag">The header.</param>
	/// <returns>The revision, or <see langword="null"/> when the header is missing or is not a number in quotes.</returns>
	private static long? ReadRevision(EntityTagHeaderValue? tag)
	{
		var text = tag?.Tag.Trim('"');
		return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ? revision : null;
	}

	/// <summary>
	/// Builds the failure for an exception of the HTTP client: a lost session is reported as it is, anything else is a network problem.
	/// </summary>
	/// <param name="exception">The exception.</param>
	/// <returns>The error.</returns>
	private static Error ErrorOf(HttpRequestException exception) => exception is TokenRefreshException refresh ? refresh.Error : AccountErrors.Network();
	#endregion
}
