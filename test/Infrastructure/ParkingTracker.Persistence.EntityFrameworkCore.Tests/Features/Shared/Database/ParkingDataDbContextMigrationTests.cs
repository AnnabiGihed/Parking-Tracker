using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Tests.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for the migrations of <see cref="ParkingDataDbContext"/> on a real PostGIS server (ADR-0025): InitialCreate
///              applies to an empty database and leaves the extension behind, the model has no change that lacks a migration, and
///              the two roles of the server behave as designed: the owner migrates, the less-privileged role serves but cannot
///              change the schema.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class ParkingDataDbContextMigrationTests
{
	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingDataDbContextMigrationTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public ParkingDataDbContextMigrationTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Migration Tests
	/// <summary>
	/// Verifies that the migrations apply on an empty database and that the PostGIS extension exists afterwards.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task InitialCreate_EmptyDatabase_ShouldApplyAndCreatePostgis()
	{
		var database = await server.CreateEmptyDatabaseAsync();
		await using var context = CreateContext(database.AdminConnectionString);

		await context.Database.MigrateAsync();

		var applied = await context.Database.GetAppliedMigrationsAsync();
		applied.Should().HaveCount(2);
		applied.First().Should().EndWith("_InitialCreate");
		applied.Last().Should().EndWith("_AddAccounts");
		var extensions = await context.Database.SqlQueryRaw<string>("SELECT extname AS \"Value\" FROM pg_extension").ToListAsync();
		extensions.Should().Contain("postgis");
	}

	/// <summary>
	/// Verifies that the model has no change that is missing from the migrations, the equivalent of
	/// <c>dotnet ef migrations has-pending-model-changes</c>.
	/// </summary>
	[Fact]
	public void Model_ShouldHaveNoPendingChanges()
	{
		using var context = CreateContext("Host=localhost;Database=parking;Username=u;Password=p");

		context.Database.HasPendingModelChanges().Should().BeFalse("a model change needs its migration");
	}

	/// <summary>
	/// Verifies that the owner role can migrate a database whose extension the superuser already created, as on the server.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task InitialCreate_AsOwnerRole_ShouldApply()
	{
		var database = await server.CreateDatabaseWithRolesAsync();
		await using var context = CreateContext(database.OwnerConnectionString);

		await context.Database.MigrateAsync();

		(await context.Database.GetAppliedMigrationsAsync()).Should().HaveCount(2);
	}

	/// <summary>
	/// Verifies that the less-privileged role can connect and read but cannot create a table, so a host that tries to migrate fails.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApiRole_AfterMigration_ShouldServeButNotChangeTheSchema()
	{
		var database = await server.CreateDatabaseWithRolesAsync();
		await using var owner = CreateContext(database.OwnerConnectionString);
		await owner.Database.MigrateAsync();

		await using var connection = new NpgsqlConnection(database.ApiConnectionString);
		await connection.OpenAsync();
		await using var read = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\"", connection);
		(await read.ExecuteScalarAsync()).Should().Be(2L);
		await using var create = new NpgsqlCommand("CREATE TABLE intruder (id int)", connection);
		var act = async () => await create.ExecuteNonQueryAsync().ConfigureAwait(false);
		(await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the context the way the design-time factory does, on the given connection string.
	/// </summary>
	/// <param name="connectionString">The connection string.</param>
	/// <returns>The context.</returns>
	private static ParkingDataDbContext CreateContext(string connectionString)
	{
		var options = new DbContextOptionsBuilder<ParkingDataDbContext>()
			.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
			.Options;
		return new ParkingDataDbContext(options);
	}
	#endregion
}
