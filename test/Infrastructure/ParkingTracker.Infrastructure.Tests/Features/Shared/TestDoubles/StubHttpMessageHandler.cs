using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net;
using System.Text;

namespace ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : An HTTP handler that answers every request from a function and keeps the requests it received, so a test can
///              prove the exact method, address and headers a client sent without touching the network.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
	#region Fields
	/// <summary>
	/// The function that builds every response.
	/// </summary>
	private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

	/// <summary>
	/// Addresses of the requests received, in arrival order.
	/// </summary>
	private readonly ConcurrentQueue<string> requests = new();
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="StubHttpMessageHandler"/> class.
	/// </summary>
	/// <param name="respond">Gives the answer to a request; it receives the cancellation token of the call.</param>
	public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
	{
		this.respond = respond;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets a snapshot of the requests received so far, as the method and the address exactly as sent, in order of arrival.
	/// </summary>
	public IReadOnlyList<string> Requests => [.. requests];

	/// <summary>
	/// Gets the headers of the last request received.
	/// </summary>
	public HttpRequestHeaders? LastHeaders { get; private set; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates a handler that answers every request with the same status and text.
	/// </summary>
	/// <param name="status">The HTTP status.</param>
	/// <param name="body">Response body text.</param>
	/// <returns>Handler that always answers with the given status and body.</returns>
	public static StubHttpMessageHandler Always(HttpStatusCode status, string body) => new((_, _) => Task.FromResult(Text(status, body)));

	/// <summary>
	/// Creates a handler that never answers until the call is cancelled, as a service that does not respond.
	/// </summary>
	/// <returns>Handler that never answers until the call is cancelled.</returns>
	public static StubHttpMessageHandler Silent()
	{
		return new StubHttpMessageHandler(async (_, token) =>
		{
			await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
			return new HttpResponseMessage(HttpStatusCode.OK);
		});
	}

	/// <summary>
	/// Builds an answer with a text body.
	/// </summary>
	/// <param name="status">The HTTP status.</param>
	/// <param name="body">Response body text.</param>
	/// <returns>Response with the given status and body.</returns>
	public static HttpResponseMessage Text(HttpStatusCode status, string body)
	{
		return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		requests.Enqueue($"{request.Method} {request.RequestUri!.OriginalString}");
		LastHeaders = request.Headers;
		return respond(request, cancellationToken);
	}
	#endregion
}
