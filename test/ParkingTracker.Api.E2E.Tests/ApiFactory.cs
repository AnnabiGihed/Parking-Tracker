using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ParkingTracker.ApiService;

namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Starts the real API composition in memory for one test, in the environment and on the connection string the test
///              names. The connection string is set explicitly, even when empty, so a variable on the developer machine never
///              reaches the test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
	#region Fields
	/// <summary>
	/// The hosting environment of the run.
	/// </summary>
	private readonly string environment;

	/// <summary>
	/// The connection string of the parking database; empty for none.
	/// </summary>
	private readonly string connectionString;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ApiFactory"/> class.
	/// </summary>
	/// <param name="environment">The hosting environment, for example Development or Production.</param>
	/// <param name="connectionString">The connection string of the parking database; empty for none.</param>
	public ApiFactory(string environment, string connectionString)
	{
		this.environment = environment;
		this.connectionString = connectionString;
	}
	#endregion

	#region Protected Methods
	/// <inheritdoc />
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		_ = builder.UseEnvironment(environment);
		_ = builder.UseSetting("ConnectionStrings:ParkingData", connectionString);
	}
	#endregion
}
