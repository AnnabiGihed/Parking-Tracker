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
///              <see cref="TokenRefreshException"/>, which the client reports with the reason. Only requests without a body are
///              retried, because a body cannot be sent twice.
/// </summary>
internal sealed class AuthenticatedHandler : DelegatingHandler
{
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

		var retry = request.Content is null ? CloneWithoutBody(request) : null;
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.Value);
		var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (response.StatusCode != HttpStatusCode.Unauthorized || retry is null)
		{
			retry?.Dispose();
			return response;
		}

		response.Dispose();
		using (retry)
		{
			var second = await refresher.RefreshAfterRejectionAsync(first.Value, cancellationToken).ConfigureAwait(false);
			if (second.IsFailure)
			{
				return Unauthorized(request, second.Error);
			}

			retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", second.Value);
			return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
		}
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
	/// Copies a request that has no body, so that it can be sent a second time.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <returns>The copy, with the same method, address, version and headers.</returns>
	private static HttpRequestMessage CloneWithoutBody(HttpRequestMessage request)
	{
		var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
		foreach (var header in request.Headers)
		{
			_ = copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		return copy;
	}
	#endregion
}
