using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ParkingTracker.ApiService;

namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Starts the real API composition in memory for one test, in the environment and on the connection string the test
///              names. The connection string and the proxy key are set explicitly, even when empty, so a variable on the developer
///              machine never reaches the test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
	#region Constants
	/// <summary>
	/// The proxy key the tests configure unless they name another or none.
	/// </summary>
	public const string DefaultProxyKey = "test-proxy-key";
	#endregion

	#region Fields
	/// <summary>
	/// The hosting environment of the run.
	/// </summary>
	private readonly string environment;

	/// <summary>
	/// The connection string of the parking database; empty for none.
	/// </summary>
	private readonly string connectionString;

	/// <summary>
	/// The proxy key; empty for none.
	/// </summary>
	private readonly string proxyKey;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ApiFactory"/> class.
	/// </summary>
	/// <param name="environment">The hosting environment, for example Development or Production.</param>
	/// <param name="connectionString">The connection string of the parking database; empty for none.</param>
	/// <param name="proxyKey">The proxy key; empty for none. Production refuses to start without one.</param>
	public ApiFactory(string environment, string connectionString, string proxyKey = DefaultProxyKey)
	{
		this.environment = environment;
		this.connectionString = connectionString;
		this.proxyKey = proxyKey;
	}
	#endregion

	#region Protected Methods
	/// <inheritdoc />
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		_ = builder.UseEnvironment(environment);
		_ = builder.UseSetting("ConnectionStrings:ParkingData", connectionString);
		_ = builder.UseSetting("ReverseProxy:Key", proxyKey);
	}
	#endregion
}
