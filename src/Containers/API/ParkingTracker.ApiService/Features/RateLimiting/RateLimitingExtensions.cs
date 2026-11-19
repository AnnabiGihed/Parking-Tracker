using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ParkingTracker.ApiService.Features.RateLimiting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the rate limits of the API: a global concurrency limit and the per-address policy of the health endpoints.
///              A refusal answers 429 with a Retry-After header in seconds and a problem document.
/// </summary>
public static class RateLimitingExtensions
{
	#region Public Methods
	/// <summary>
	/// Registers the rate limiter with the global limit, the named policies and the refusal answer.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <returns>The same service collection.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
	public static IServiceCollection AddParkingRateLimiting(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		return services.AddRateLimiter(options =>
		{
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			options.OnRejected = WriteRejectionAsync;
			options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetConcurrencyLimiter(
				"global",
				_ => new ConcurrencyLimiterOptions { PermitLimit = RateLimitSettings.GlobalConcurrencyLimit, QueueLimit = 0 }));
			_ = options.AddPolicy(RateLimitSettings.HealthPolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
				ClientAddressKey.From(context.Connection.RemoteIpAddress),
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = RateLimitSettings.HealthPermitsPerWindow,
					Window = RateLimitSettings.HealthWindow,
					QueueLimit = 0,
				}));
		});
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Answers a refused request with 429, the Retry-After header and a problem document.
	/// </summary>
	/// <param name="context">The rejection context.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>A task that completes when the answer is written.</returns>
	private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
	{
		var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
			? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
			: RateLimitSettings.DefaultRetryAfterSeconds;
		var response = context.HttpContext.Response;
		response.StatusCode = StatusCodes.Status429TooManyRequests;
		response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
		var problem = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests" };
		await response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken).ConfigureAwait(false);
	}
	#endregion
}
