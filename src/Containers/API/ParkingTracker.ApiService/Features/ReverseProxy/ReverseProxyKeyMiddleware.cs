using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ParkingTracker.ApiService.Features.ReverseProxy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Lets the forwarded headers through only on a request that carries the proxy key (ADR-0029). It runs before the
///              forwarded-headers middleware: a request with the right key keeps X-Forwarded-For, -Proto and -Host; any other request
///              loses them, so a neighbour on the shared network cannot choose the client address. The key header never goes further.
/// </summary>
public sealed class ReverseProxyKeyMiddleware
{
	#region Constants
	/// <summary>
	/// The header in which the shared proxy sends the key.
	/// </summary>
	public const string KeyHeaderName = "X-PT-Proxy-Key";
	#endregion

	#region Fields
	/// <summary>
	/// The forwarded headers that only the proxy may set.
	/// </summary>
	private static readonly string[] ForwardedHeaderNames =
	[
		ForwardedHeadersDefaults.XForwardedForHeaderName,
		ForwardedHeadersDefaults.XForwardedProtoHeaderName,
		ForwardedHeadersDefaults.XForwardedHostHeaderName,
	];

	/// <summary>
	/// The next step of the pipeline.
	/// </summary>
	private readonly RequestDelegate next;

	/// <summary>
	/// The configured key as bytes; empty when none is configured, which no request matches.
	/// </summary>
	private readonly byte[] expectedKey;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ReverseProxyKeyMiddleware"/> class.
	/// </summary>
	/// <param name="next">The next step of the pipeline.</param>
	/// <param name="options">The proxy settings.</param>
	public ReverseProxyKeyMiddleware(RequestDelegate next, IOptions<ReverseProxyOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);
		this.next = next;
		expectedKey = Encoding.UTF8.GetBytes(options.Value.Key ?? string.Empty);
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Filters the forwarded headers of one request, then continues the pipeline.
	/// </summary>
	/// <param name="context">The HTTP context.</param>
	/// <returns>A task that completes when the rest of the pipeline has run.</returns>
	public Task InvokeAsync(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		var trusted = CarriesTheKey(context.Request.Headers[KeyHeaderName]);
		_ = context.Request.Headers.Remove(KeyHeaderName);
		if (!trusted)
		{
			foreach (var name in ForwardedHeaderNames)
			{
				_ = context.Request.Headers.Remove(name);
			}
		}

		return next(context);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Tells whether the header holds exactly the configured key, compared in constant time.
	/// </summary>
	/// <param name="values">The values of the key header.</param>
	/// <returns><see langword="true"/> when a key is configured and the single value equals it.</returns>
	private bool CarriesTheKey(StringValues values)
	{
		if (expectedKey.Length == 0 || values.Count != 1)
		{
			return false;
		}

		return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(values[0]!), expectedKey);
	}
	#endregion
}
