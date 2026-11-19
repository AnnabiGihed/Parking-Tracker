using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ParkingTracker.ServiceDefaults.Features.Health;
using ParkingTracker.ServiceDefaults.Features.Hosting;

namespace ParkingTracker.ServiceDefaults.Tests.Features.Hosting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for the two health endpoints of <see cref="ServiceDefaultsExtensions"/>, on an in-memory host: the liveness
///              path runs no check, the readiness path runs the checks tagged "ready" and answers a status text only. The reverse
///              proxy and the uptime monitor depend on exactly this behaviour.
/// </summary>
public sealed class HealthEndpointsTests
{
	#region Liveness Tests
	/// <summary>
	/// Verifies that the liveness path answers 200 even when a ready check fails, because it runs no check.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Live_ReadyCheckFails_ShouldStillAnswer200()
	{
		await using var host = await StartHostAsync(checks => checks.AddCheck("database", () => HealthCheckResult.Unhealthy(), [HealthEndpoints.ReadyTag]));

		var response = await host.Client.GetAsync(new Uri(HealthEndpoints.LivePath, UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}
	#endregion

	#region Readiness Tests
	/// <summary>
	/// Verifies that the readiness path answers 200 when no check is registered.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Ready_NoChecks_ShouldAnswer200()
	{
		await using var host = await StartHostAsync(_ => { });

		var response = await host.Client.GetAsync(new Uri(HealthEndpoints.ReadyPath, UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// Verifies that the readiness path answers 200 when its check passes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Ready_ReadyCheckPasses_ShouldAnswer200()
	{
		await using var host = await StartHostAsync(checks => checks.AddCheck("database", () => HealthCheckResult.Healthy(), [HealthEndpoints.ReadyTag]));

		var response = await host.Client.GetAsync(new Uri(HealthEndpoints.ReadyPath, UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
	}

	/// <summary>
	/// Verifies that the readiness path answers 503 with the status text only when a ready check fails, and that the description
	/// of the failure does not leave the process.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Ready_ReadyCheckFails_ShouldAnswer503WithStatusOnly()
	{
		await using var host = await StartHostAsync(checks => checks.AddCheck("database", () => HealthCheckResult.Unhealthy("password=hunter2"), [HealthEndpoints.ReadyTag]));

		var response = await host.Client.GetAsync(new Uri(HealthEndpoints.ReadyPath, UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
		(await response.Content.ReadAsStringAsync()).Should().Be("Unhealthy");
	}

	/// <summary>
	/// Verifies that a failing check without the ready tag does not affect the readiness path.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Ready_FailingCheckWithoutReadyTag_ShouldBeIgnored()
	{
		await using var host = await StartHostAsync(checks => checks.AddCheck("other", () => HealthCheckResult.Unhealthy()));

		var response = await host.Client.GetAsync(new Uri(HealthEndpoints.ReadyPath, UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}
	#endregion

	#region Guard Tests
	/// <summary>
	/// Verifies that a missing builder or endpoint route builder is rejected.
	/// </summary>
	[Fact]
	public void Extensions_NullArguments_ShouldThrow()
	{
		var noBuilder = () => ServiceDefaultsExtensions.AddServiceDefaults<WebApplicationBuilder>(null!); // null on purpose: proves the guard clause rejects it
		var noEndpoints = () => ServiceDefaultsExtensions.MapDefaultEndpoints(null!); // null on purpose: proves the guard clause rejects it

		noBuilder.Should().Throw<ArgumentNullException>();
		noEndpoints.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Starts an in-memory host with the service defaults, the given checks and the default endpoints.
	/// </summary>
	/// <param name="addChecks">Adds the health checks of the scenario.</param>
	/// <returns>The running host.</returns>
	private static async Task<RunningHost> StartHostAsync(Action<IHealthChecksBuilder> addChecks)
	{
		var builder = WebApplication.CreateBuilder();
		_ = builder.WebHost.UseTestServer();
		_ = builder.AddServiceDefaults();
		addChecks(builder.Services.AddHealthChecks());
		var app = builder.Build();
		_ = app.MapDefaultEndpoints();
		await app.StartAsync().ConfigureAwait(false);
		return new RunningHost(app);
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : A started web application with the client of its in-memory server; disposing it stops the
	///              application.
	/// </summary>
	private sealed class RunningHost : IAsyncDisposable
	{
		#region Fields
		/// <summary>
		/// The started application.
		/// </summary>
		private readonly WebApplication app;
		#endregion

		#region Properties
		/// <summary>
		/// Gets the client of the in-memory server.
		/// </summary>
		public HttpClient Client { get; }
		#endregion

		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="RunningHost"/> class around a started application.
		/// </summary>
		/// <param name="app">The started application.</param>
		public RunningHost(WebApplication app)
		{
			this.app = app;
			Client = app.GetTestClient();
		}
		#endregion

		#region IAsyncDisposable Implementation
		/// <inheritdoc />
		public async ValueTask DisposeAsync()
		{
			Client.Dispose();
			await this.app.DisposeAsync().ConfigureAwait(false);
		}
		#endregion
	}
	#endregion
}
