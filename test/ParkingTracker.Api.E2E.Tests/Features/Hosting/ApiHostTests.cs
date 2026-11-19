using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ParkingTracker.ApiService.Features.Hosting;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;
using ParkingTracker.ServiceDefaults.Features.Health;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Api.E2E.Tests.Features.Hosting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : End-to-end tests of the API host (ADR-0016, ADR-0023, ADR-0025), on the real composition in memory and a real PostGIS
///              server. The database is migrated as the owner role and the API serves as the less-privileged role, so a missing
///              grant shows here. They check the contract the deployment relies on: the readiness answer with and without the
///              database, no interactive reference in Production, and the generated OpenAPI document against the committed one.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class ApiHostTests
{
	#region Constants
	/// <summary>
	/// A connection string to a port nothing listens on, with a short timeout.
	/// </summary>
	private const string UnreachableConnection = "Host=127.0.0.1;Port=1;Database=parking;Username=u;Password=p;Timeout=2";

	/// <summary>
	/// The committed OpenAPI document, copied to the output folder of the tests.
	/// </summary>
	private const string SnapshotPath = "Snapshots/openapi.v1.json";
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ApiHostTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public ApiHostTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Health Tests
	/// <summary>
	/// Verifies that the readiness path answers 200 with the status text only when the database is migrated and reachable by the
	/// less-privileged role, in Production.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Ready_DatabaseReachable_ShouldAnswer200()
	{
		var database = await MigrateAsOwnerAsync();
		await using var factory = new ApiFactory("Production", database.ApiConnectionString);
		using var client = factory.CreateClient();

		var response = await client.GetAsync(new Uri(HealthEndpoints.ReadyPath, UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
	}

	/// <summary>
	/// Verifies that the readiness path answers 503 with the status text only when the database cannot be reached, and that the
	/// liveness path still answers 200.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Ready_DatabaseUnreachable_ShouldAnswer503AndLiveStaysUp()
	{
		await using var factory = new ApiFactory("Production", UnreachableConnection);
		using var client = factory.CreateClient();

		var ready = await client.GetAsync(new Uri(HealthEndpoints.ReadyPath, UriKind.Relative));
		var live = await client.GetAsync(new Uri(HealthEndpoints.LivePath, UriKind.Relative));

		ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
		(await ready.Content.ReadAsStringAsync()).Should().Be("Unhealthy");
		live.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// Verifies that the host refuses to start without a connection string, with a message that says what to set.
	/// </summary>
	[Fact]
	public void Start_WithoutConnectionString_ShouldFailWithAClearMessage()
	{
		using var factory = new ApiFactory("Production", string.Empty);

		var act = () => factory.CreateClient();

		act.Should().Throw<InvalidOperationException>().WithMessage($"*{ApiHostExtensions.ConnectionStringName}*");
	}
	#endregion

	#region Interactive Reference Tests
	/// <summary>
	/// Verifies that the OpenAPI document and the Scalar page are served in Development.
	/// </summary>
	/// <param name="path">The path to request.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("/swagger/v1/swagger.json")]
	[InlineData("/scalar")]
	public async Task Reference_Development_ShouldBeServed(string path)
	{
		await using var factory = new ApiFactory("Development", UnreachableConnection);
		using var client = factory.CreateClient();

		var response = await client.GetAsync(new Uri(path, UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// Verifies that neither the OpenAPI document nor Scalar is mapped in Production.
	/// </summary>
	/// <param name="path">The path to request.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("/swagger/v1/swagger.json")]
	[InlineData("/swagger")]
	[InlineData("/scalar")]
	[InlineData("/scalar/v1")]
	public async Task Reference_Production_ShouldAnswer404(string path)
	{
		await using var factory = new ApiFactory("Production", UnreachableConnection);
		using var client = factory.CreateClient();

		var response = await client.GetAsync(new Uri(path, UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
	#endregion

	#region Contract Tests
	/// <summary>
	/// Verifies that the generated OpenAPI document equals the committed snapshot, so a change of the contract shows in the pull
	/// request. To accept a change, replace the snapshot with the document this test prints.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OpenApiDocument_ShouldEqualTheCommittedSnapshot()
	{
		await using var factory = new ApiFactory("Development", UnreachableConnection);
		using var client = factory.CreateClient();

		var generated = JsonNode.Parse(await client.GetStringAsync(new Uri(ApiHostExtensions.OpenApiRoute, UriKind.Relative)));
		var committed = JsonNode.Parse(await File.ReadAllTextAsync(SnapshotPath));

		JsonNode.DeepEquals(generated, committed).Should().BeTrue("the OpenAPI contract changed; generated document:\n{0}", generated?.ToJsonString());
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Creates a database laid out like the server and migrates it as the owner role.
	/// </summary>
	/// <returns>The database with the connection strings of all three users.</returns>
	private async Task<TestDatabase> MigrateAsOwnerAsync()
	{
		var database = await server.CreateDatabaseWithRolesAsync().ConfigureAwait(false);
		var options = new DbContextOptionsBuilder<ParkingDataDbContext>()
			.UseNpgsql(database.OwnerConnectionString, npgsql => npgsql.UseNetTopologySuite())
			.Options;
		using var context = new ParkingDataDbContext(options);
		await context.Database.MigrateAsync().ConfigureAwait(false);
		return database;
	}
	#endregion
}
