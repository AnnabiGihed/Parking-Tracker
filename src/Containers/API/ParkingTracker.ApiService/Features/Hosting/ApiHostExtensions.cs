using Asp.Versioning;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using ParkingTracker.ApiService.Features.RateLimiting;
using ParkingTracker.ApiService.Features.ReverseProxy;
using ParkingTracker.Persistence.EntityFrameworkCore;
using ParkingTracker.ServiceDefaults.Features.Health;
using ParkingTracker.ServiceDefaults.Features.Hosting;
using Pivot.Framework.Containers.API.Health;
using Pivot.Framework.Containers.API.Middleware;
using Pivot.Framework.Containers.API.Versioning;
using Scalar.AspNetCore;

namespace ParkingTracker.ApiService.Features.Hosting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The composition of the API host (ADR-0016, ADR-0023, ADR-0028). The registrations: the service defaults, the
///              connection string (from configuration or the server's secret file), the database context, the readiness check on
///              the database, API versioning, controllers and the OpenAPI document "v1". The pipeline: Pivot's exception handler
///              first, the proxy-key filter and the forwarded headers (ADR-0029), routing, the rate limiter, the two health endpoints in
///              every environment under their rate limit, and the interactive reference (OpenAPI document and Scalar) in Development only.
/// </summary>
public static class ApiHostExtensions
{
	#region Constants
	/// <summary>
	/// The folder where the server mounts its secrets as one file per key, named like the configuration key with "__" for ":".
	/// </summary>
	public const string SecretsDirectory = "/run/secrets";

	/// <summary>
	/// The name of the connection string of the parking database.
	/// </summary>
	public const string ConnectionStringName = "ParkingData";

	/// <summary>
	/// The name of the OpenAPI document.
	/// </summary>
	public const string DocumentName = "v1";

	/// <summary>
	/// The route of the generated OpenAPI document.
	/// </summary>
	public const string OpenApiRoute = "/swagger/v1/swagger.json";
	#endregion

	#region Public Methods
	/// <summary>
	/// Registers everything the API needs.
	/// </summary>
	/// <param name="builder">The web application builder.</param>
	/// <returns>The same builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">No connection string is configured for the parking database.</exception>
	public static WebApplicationBuilder AddParkingApi(this WebApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		_ = builder.Configuration.AddKeyPerFile(SecretsDirectory, optional: true);
		_ = builder.AddServiceDefaults();
		var connectionString = builder.Configuration.GetConnectionString(ConnectionStringName);
		if (string.IsNullOrWhiteSpace(connectionString))
		{
			throw new InvalidOperationException(
				$"The connection string '{ConnectionStringName}' is not configured: set ConnectionStrings:{ConnectionStringName} or mount it as a file in {SecretsDirectory}.");
		}

		_ = builder.Services.AddParkingDataPersistence(connectionString);
		_ = builder.Services.AddHealthChecks().AddPostgreSqlHealthCheck(connectionString);
		TagRegistrationsAsReady(builder.Services);
		_ = builder.Services.AddPivotApiVersioning();
		_ = builder.Services.AddControllers();
		_ = builder.Services.AddSwaggerGen(options => options.SwaggerDoc(DocumentName, new OpenApiInfo { Title = "Parking Tracker API", Version = DocumentName }));
		_ = builder.Services.AddReverseProxyTrust(builder.Configuration);
		_ = builder.Services.AddParkingRateLimiting();
		return builder;
	}

	/// <summary>
	/// Builds the request pipeline.
	/// </summary>
	/// <param name="app">The web application.</param>
	/// <returns>The same web application.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
	public static WebApplication UseParkingApi(this WebApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		_ = app.UseMiddleware<ExceptionHandlerMiddleware>();
		_ = app.UseReverseProxyTrust();
		_ = app.UseRouting();
		_ = app.UseRateLimiter();
		var health = app.MapGroup(string.Empty).RequireRateLimiting(RateLimitSettings.HealthPolicyName);
		_ = health.MapDefaultEndpoints();
		_ = app.MapControllers();
		if (app.Environment.IsDevelopment())
		{
			_ = app.UseSwagger();
			_ = app.MapScalarApiReference(options => options.WithOpenApiRoutePattern(OpenApiRoute));
		}

		return app;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Puts every registered health check in the readiness endpoint. Pivot's database check takes no tags, and the database is the
	/// only check registered, so this is where it joins <see cref="HealthEndpoints.ReadyTag"/>.
	/// </summary>
	/// <param name="services">The service collection.</param>
	private static void TagRegistrationsAsReady(IServiceCollection services)
	{
		_ = services.Configure<HealthCheckServiceOptions>(options =>
		{
			foreach (var registration in options.Registrations)
			{
				_ = registration.Tags.Add(HealthEndpoints.ReadyTag);
			}
		});
	}
	#endregion
}
