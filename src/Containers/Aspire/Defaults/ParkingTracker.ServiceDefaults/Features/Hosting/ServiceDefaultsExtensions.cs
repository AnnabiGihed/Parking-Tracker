using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ParkingTracker.ServiceDefaults.Features.Health;

namespace ParkingTracker.ServiceDefaults.Features.Hosting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The defaults every back-end host applies: OpenTelemetry (exported over OTLP only when the environment names an
///              endpoint), service discovery, the standard resilience handler on HTTP clients and the two health endpoints. A host
///              calls <see cref="AddServiceDefaults{TBuilder}"/> while it builds and <see cref="MapDefaultEndpoints"/> when it maps.
/// </summary>
public static class ServiceDefaultsExtensions
{
	#region Constants
	/// <summary>
	/// The standard OpenTelemetry variable that names the OTLP collector; the exporter is added only when it is set.
	/// </summary>
	private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
	#endregion

	#region Public Methods
	/// <summary>
	/// Adds telemetry, service discovery and the resilience handler for HTTP clients to the host.
	/// </summary>
	/// <typeparam name="TBuilder">The type of the host builder.</typeparam>
	/// <param name="builder">The host builder.</param>
	/// <returns>The same builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
	public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		ArgumentNullException.ThrowIfNull(builder);
		ConfigureOpenTelemetry(builder);
		_ = builder.Services.AddHealthChecks();
		_ = builder.Services.AddServiceDiscovery();
		_ = builder.Services.ConfigureHttpClientDefaults(http =>
		{
			// Every outbound call of the back end so far is an idempotent read of a public source, so retries are safe. A client
			// that sends a non-idempotent request has to opt out of this handler.
			_ = http.AddStandardResilienceHandler();
			_ = http.AddServiceDiscovery();
		});
		return builder;
	}

	/// <summary>
	/// Maps <see cref="HealthEndpoints.LivePath"/> and <see cref="HealthEndpoints.ReadyPath"/> in every environment. Both answer a
	/// status text only, never the details of a check.
	/// </summary>
	/// <param name="endpoints">The endpoint route builder, usually the web application.</param>
	/// <returns>The same endpoint route builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
	public static IEndpointRouteBuilder MapDefaultEndpoints(this IEndpointRouteBuilder endpoints)
	{
		ArgumentNullException.ThrowIfNull(endpoints);
		_ = endpoints.MapHealthChecks(HealthEndpoints.LivePath, new HealthCheckOptions { Predicate = _ => false });
		_ = endpoints.MapHealthChecks(
			HealthEndpoints.ReadyPath,
			new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(HealthEndpoints.ReadyTag) });
		return endpoints;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Adds logging, metrics and traces; health probes are kept out of the traces.
	/// </summary>
	/// <typeparam name="TBuilder">The type of the host builder.</typeparam>
	/// <param name="builder">The host builder.</param>
	private static void ConfigureOpenTelemetry<TBuilder>(TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		_ = builder.Logging.AddOpenTelemetry(logging =>
		{
			logging.IncludeFormattedMessage = true;
			logging.IncludeScopes = true;
		});
		var telemetry = builder.Services.AddOpenTelemetry()
			.WithMetrics(metrics => metrics
				.AddAspNetCoreInstrumentation()
				.AddHttpClientInstrumentation()
				.AddRuntimeInstrumentation())
			.WithTracing(tracing => tracing
				.AddAspNetCoreInstrumentation(options => options.Filter = context => !IsHealthRequest(context.Request.Path))
				.AddHttpClientInstrumentation());
		if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
		{
			_ = telemetry.UseOtlpExporter();
		}
	}

	/// <summary>
	/// Tells whether a request path belongs to the health endpoints.
	/// </summary>
	/// <param name="path">The request path.</param>
	/// <returns><see langword="true"/> for a health path.</returns>
	private static bool IsHealthRequest(PathString path)
	{
		return path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
	}
	#endregion
}
