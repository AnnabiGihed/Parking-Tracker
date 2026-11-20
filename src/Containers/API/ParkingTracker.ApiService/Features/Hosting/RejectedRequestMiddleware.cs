using Microsoft.AspNetCore.Mvc;

namespace ParkingTracker.ApiService.Features.Hosting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Answers a request that the server itself refused while the body was read, such as one above the size limit of its
///              endpoint, with the status the server chose (413 for a body that is too large, otherwise the one it gives) and a
///              problem document. Without it Pivot exception handler, which sits outside, would turn the refusal into a 500. The
///              message of the exception is never sent.
/// </summary>
public sealed class RejectedRequestMiddleware
{
	#region Fields
	/// <summary>
	/// The next step of the pipeline.
	/// </summary>
	private readonly RequestDelegate next;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="RejectedRequestMiddleware"/> class.
	/// </summary>
	/// <param name="next">The next step of the pipeline.</param>
	public RejectedRequestMiddleware(RequestDelegate next)
	{
		this.next = next;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Runs the rest of the pipeline and answers a refusal of the server while the request was read.
	/// </summary>
	/// <param name="context">The request.</param>
	/// <returns>A task that completes when the request is answered.</returns>
	public async Task InvokeAsync(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		try
		{
			await next(context).ConfigureAwait(false);
		}
		catch (BadHttpRequestException refusal) when (!context.Response.HasStarted)
		{
			context.Response.StatusCode = refusal.StatusCode;
			var problem = new ProblemDetails
			{
				Status = refusal.StatusCode,
				Title = refusal.StatusCode == StatusCodes.Status413PayloadTooLarge ? "Request body too large" : "Bad request",
			};

			// Not the request token: a client that dropped the connection mid-body would otherwise turn the 4xx into an exception.
			await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", CancellationToken.None).ConfigureAwait(false);
		}
	}
	#endregion
}
