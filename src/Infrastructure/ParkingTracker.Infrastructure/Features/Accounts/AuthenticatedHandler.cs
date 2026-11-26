using System.Net;
using System.Net.Http.Headers;
using ParkingTracker.Application.Features.Accounts.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Adds the bearer token to every request of the signed-in user (ADR-0015). When the API answers 401 it asks the refresher for a
///              new token and sends the request once more; a second 401 is returned as it is. Without a session no request is sent and the
///              answer is a 401. When the token cannot be refreshed because the network or the server failed, the handler throws
///              <see cref="TokenRefreshException"/>, which the client reports with the reason. A request with a body is retried only
///              when the body fits in <see cref="MaxRetryBodyBytes"/>: it is buffered before the first send and copied into the retry.
///              A larger body, or one that cannot be read twice, is sent once and its 401 is returned as it is.
/// </summary>
internal sealed class AuthenticatedHandler : DelegatingHandler
{
	#region Constants
	/// <summary>
	/// The largest body, in bytes, that is kept in memory so that the request can be sent again after a 401 (64 KB).
	/// </summary>
	internal const int MaxRetryBodyBytes = 64 * 1024;
	#endregion

	#region Fields
	/// <summary>
	/// Gives and renews the access token.
	/// </summary>
	private readonly AccessTokenRefresher refresher;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AuthenticatedHandler"/> class.
	/// </summary>
	/// <param name="refresher">Gives and renews the access token.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="refresher"/> is <see langword="null"/>.</exception>
	public AuthenticatedHandler(AccessTokenRefresher refresher)
	{
		ArgumentNullException.ThrowIfNull(refresher);
		this.refresher = refresher;
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var first = await refresher.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
		if (first.IsFailure)
		{
			return Unauthorized(request, first.Error);
		}

		using var retry = await CloneForRetryAsync(request, cancellationToken).ConfigureAwait(false);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.Value);
		var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (response.StatusCode != HttpStatusCode.Unauthorized || retry is null)
		{
			return response;
		}

		response.Dispose();
		var second = await refresher.RefreshAfterRejectionAsync(first.Value, cancellationToken).ConfigureAwait(false);
		if (second.IsFailure)
		{
			return Unauthorized(request, second.Error);
		}

		retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", second.Value);
		return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Answers a request that cannot be authenticated: a 401 when the user is signed out, an exception when the network failed.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <param name="error">Why no token could be had.</param>
	/// <returns>The 401 answer.</returns>
	/// <exception cref="TokenRefreshException">Thrown when the failure is not that the user is signed out.</exception>
	private static HttpResponseMessage Unauthorized(HttpRequestMessage request, Error error)
	{
		if (!string.Equals(error.Code, AccountErrors.NotSignedInCode, StringComparison.Ordinal))
		{
			throw new TokenRefreshException(error);
		}

		return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
	}

	/// <summary>
	/// Copies a request so that it can be sent a second time, with a copy of its body when it has one that can be kept in memory.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <param name="cancellationToken">A token to cancel the reading of the body.</param>
	/// <returns>A task with the copy, or <see langword="null"/> when the body cannot be sent twice.</returns>
	private static async Task<HttpRequestMessage?> CloneForRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Content is null)
		{
			return CloneWithoutBody(request);
		}

		var body = await TryBufferBodyAsync(request.Content, cancellationToken).ConfigureAwait(false);
		if (body is null)
		{
			return null;
		}

		var copy = CloneWithoutBody(request);
		copy.Content = body;
		return copy;
	}

	/// <summary>
	/// Buffers a body of at most <see cref="MaxRetryBodyBytes"/> bytes and copies it, with its content headers, into a new body.
	/// </summary>
	/// <param name="content">The body of the request; once buffered it is sent from memory.</param>
	/// <param name="cancellationToken">A token to cancel the reading of the body.</param>
	/// <returns>A task with the copy, or <see langword="null"/> when the body is too large or cannot be buffered.</returns>
	private static async Task<ByteArrayContent?> TryBufferBodyAsync(HttpContent content, CancellationToken cancellationToken)
	{
		if (!CanBufferWithoutConsuming(content))
		{
			return null;
		}

		try
		{
			await content.LoadIntoBufferAsync(MaxRetryBodyBytes, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException)
		{
			// The body is larger than the limit or could not be read; it is sent once, as before, and a 401 is returned as it is.
			return null;
		}

		var bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
		var copy = new ByteArrayContent(bytes);
		foreach (var header in content.Headers)
		{
			_ = copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		return copy;
	}

	/// <summary>
	/// Tells whether a body can be buffered without risking the first send: its length is known and within the limit, or it is content
	/// that is serialised again when a buffering attempt stops at the limit (such as JSON or text).
	/// </summary>
	/// <param name="content">The body of the request.</param>
	/// <returns><see langword="true"/> when buffering is tried; <see langword="false"/> when the request is only sent once.</returns>
	private static bool CanBufferWithoutConsuming(HttpContent content)
	{
		var length = content.Headers.ContentLength;
		if (length is not null)
		{
			return length <= MaxRetryBodyBytes;
		}

		// A stream of unknown length cannot be rewound: a buffering attempt that stopped at the limit would leave nothing to send.
		return content is not StreamContent and not MultipartContent;
	}

	/// <summary>
	/// Copies a request without its body.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <returns>The copy, with the same method, address, version, headers and options.</returns>
	private static HttpRequestMessage CloneWithoutBody(HttpRequestMessage request)
	{
		var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
		foreach (var header in request.Headers)
		{
			_ = copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		IDictionary<string, object?> options = copy.Options;
		foreach (var option in request.Options)
		{
			options[option.Key] = option.Value;
		}

		return copy;
	}
	#endregion
}
