using Asp.Versioning;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using ParkingTracker.ApiService.Features.Authentication;
using ParkingTracker.ApiService.Features.OpenApi;
using ParkingTracker.ApiService.Features.Profiles;
using ParkingTracker.ApiService.Features.RateLimiting;
using ParkingTracker.ApiService.Features.ReverseProxy;
using ParkingTracker.Backend.Application;
using ParkingTracker.Persistence.EntityFrameworkCore;
using ParkingTracker.ServiceDefaults.Features.Health;
using ParkingTracker.ServiceDefaults.Features.Hosting;
using Pivot.Framework.Containers.API.Health;
using Pivot.Framework.Containers.API.Middleware;
using Pivot.Framework.Containers.API.Versioning;
using Scalar.AspNetCore;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ParkingTracker.ApiService.Features.Hosting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The composition of the API host (ADR-0014, ADR-0016, ADR-0023, ADR-0028). The registrations: the service defaults, the
///              connection string (from configuration or the server's secret file), the database context, the readiness check on
///              the database, the use cases of the server, the sign-in (settings, ports, JWT bearer, fallback policy), API versioning,
///              controllers and the OpenAPI document "v1". The pipeline: Pivot's exception handler first, the answer to a request the server refused while reading it (a body over its limit), the proxy-key filter and
///              the forwarded headers (ADR-0029), routing, authentication, the rate limiter (after authentication, so the
///              per-account limit sees the user), authorization, the two health endpoints in every environment under their rate
///              limit and anonymous, the controllers, an anonymous fallback that keeps an unknown path a 404, and the interactive
///              reference (OpenAPI document and Scalar) in Development only. No transaction middleware is in the pipeline: the
///              sign-in commits through the unit of work itself, so a consumed nonce stays consumed on a refusal.
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
		_ = builder.Services.AddBackendApplication();
		_ = builder.Services.AddParkingAuthentication(builder.Configuration);
		_ = builder.Services.AddParkingProfiles(builder.Configuration);
		_ = builder.Services.AddPivotApiVersioning();
		_ = builder.Services.AddControllers();
		_ = builder.Services.AddSwaggerGen(ConfigureOpenApi);
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
		_ = app.UseMiddleware<RejectedRequestMiddleware>();
		_ = app.UseReverseProxyTrust();
		_ = app.UseRouting();
		_ = app.UseAuthentication();
		_ = app.UseRateLimiter();
		_ = app.UseAuthorization();
		var health = app.MapGroup(string.Empty).RequireRateLimiting(RateLimitSettings.HealthPolicyName).AllowAnonymous();
		_ = health.MapDefaultEndpoints();
		_ = app.MapControllers();

		// The fallback policy would answer 401 to a path that does not exist; this anonymous endpoint keeps it a plain 404, files like swagger.json included.
		_ = app.MapFallback("{*path}", static () => Results.NotFound()).AllowAnonymous();
		if (app.Environment.IsDevelopment())
		{
			_ = app.MapSwagger().AllowAnonymous();
			_ = app.MapScalarApiReference(options => options.WithOpenApiRoutePattern(OpenApiRoute)).AllowAnonymous();
		}

		return app;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Configures the OpenAPI document: its title, the bearer scheme, the examples and required properties of the contract records.
	/// </summary>
	/// <param name="options">The generator options.</param>
	private static void ConfigureOpenApi(SwaggerGenOptions options)
	{
		options.SwaggerDoc(DocumentName, new OpenApiInfo { Title = "Parking Tracker API", Version = DocumentName });
		options.SupportNonNullableReferenceTypes();
		options.AddSecurityDefinition(
			BearerSecurityOperationFilter.SchemeName,
			new OpenApiSecurityScheme
			{
				Type = SecuritySchemeType.Http,
				Scheme = "bearer",
				BearerFormat = "JWT",
				Description = "The access token of POST /api/v1/sessions (ES256, 15 minutes).",
			});
		options.OperationFilter<BearerSecurityOperationFilter>();
		options.SchemaFilter<ContractSchemaFilter>();
		options.OperationFilter<ProblemResponseExamplesOperationFilter>();
	}

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
