using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace ParkingTracker.ApiService.Features.ReverseProxy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers and applies the trust in the shared reverse proxy (ADR-0029): the proxy settings with their start-up check,
///              the forwarded-headers options and the two middlewares in their order, the key filter first.
/// </summary>
public static class ReverseProxyExtensions
{
	#region Public Methods
	/// <summary>
	/// Registers the proxy settings, their validation at start and the forwarded-headers options.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The configuration holding the "ReverseProxy" section.</param>
	/// <returns>The same service collection.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
	public static IServiceCollection AddReverseProxyTrust(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);
		_ = services.AddOptions<ReverseProxyOptions>().Bind(configuration.GetSection(ReverseProxyOptions.SectionName)).ValidateOnStart();
		_ = services.AddSingleton<IValidateOptions<ReverseProxyOptions>, ReverseProxyOptionsValidator>();
		_ = services.Configure<ForwardedHeadersOptions>(options =>
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
			options.ForwardLimit = 1;

			// Trusting every sender is safe only because ReverseProxyKeyMiddleware has already removed the headers of any request without the key.
			options.KnownIPNetworks.Clear();
			options.KnownProxies.Clear();
		});
		return services;
	}

	/// <summary>
	/// Adds the key filter and then the forwarded-headers middleware.
	/// </summary>
	/// <param name="app">The application builder.</param>
	/// <returns>The same application builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
	public static IApplicationBuilder UseReverseProxyTrust(this IApplicationBuilder app)
	{
		ArgumentNullException.ThrowIfNull(app);
		_ = app.UseMiddleware<ReverseProxyKeyMiddleware>();
		return app.UseForwardedHeaders();
	}
	#endregion
}
