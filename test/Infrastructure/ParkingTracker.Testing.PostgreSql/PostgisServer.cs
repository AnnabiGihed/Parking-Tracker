using System.Data.Common;
using System.Globalization;
using Testcontainers.PostgreSql;
using Xunit;

namespace ParkingTracker.Testing.PostgreSql;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A throw-away PostgreSQL server with PostGIS for the tests that need the real database (ADR-0025). It creates the two
///              roles the server has: an owner that migrates and a less-privileged role that the API serves with, so a test that
///              forgets a grant fails here and not in production. Each test takes its own database, so tests do not share state.
///              The role passwords are generated for the run; nothing secret is stored.
/// </summary>
public sealed class PostgisServer : IAsyncLifetime
{
	#region Constants
	/// <summary>
	/// The image of the server, the same line as the local AppHost.
	/// </summary>
	public const string Image = "postgis/postgis:17-3.5";

	/// <summary>
	/// The name of the owner role.
	/// </summary>
	public const string OwnerRole = "pt_owner";

	/// <summary>
	/// The name of the less-privileged role.
	/// </summary>
	public const string ApiRole = "pt_api";
	#endregion

	#region Fields
	/// <summary>
	/// The container of the server.
	/// </summary>
	private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).Build();

	/// <summary>
	/// The generated password of the owner role.
	/// </summary>
	private readonly string ownerPassword = Guid.NewGuid().ToString("N");

	/// <summary>
	/// The generated password of the less-privileged role.
	/// </summary>
	private readonly string apiPassword = Guid.NewGuid().ToString("N");

	/// <summary>
	/// The number of databases made so far, used to give each a unique name.
	/// </summary>
	private int databaseCounter;
	#endregion

	#region IAsyncLifetime Implementation
	/// <inheritdoc />
	public async Task InitializeAsync()
	{
		await container.StartAsync().ConfigureAwait(false);
		await RunSqlAsync(
			"postgres",
			$"CREATE ROLE {OwnerRole} LOGIN NOSUPERUSER PASSWORD '{ownerPassword}'; CREATE ROLE {ApiRole} LOGIN NOSUPERUSER PASSWORD '{apiPassword}';").ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task DisposeAsync()
	{
		await container.DisposeAsync().ConfigureAwait(false);
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Creates an empty database owned by the superuser, without the PostGIS extension and without roles.
	/// </summary>
	/// <returns>The new database; the role strings are empty.</returns>
	public async Task<TestDatabase> CreateEmptyDatabaseAsync()
	{
		var name = NextName();
		await RunSqlAsync("postgres", $"CREATE DATABASE {name};").ConfigureAwait(false);
		return new TestDatabase(name, Connect(name, "postgres", "postgres"), string.Empty, string.Empty);
	}

	/// <summary>
	/// Creates a database laid out like the server: owned by the owner role, with the PostGIS extension created by the
	/// superuser (PostGIS is not a trusted extension, so the owner cannot create it), and with the grants the API role needs on
	/// what the owner creates later.
	/// </summary>
	/// <returns>The new database with the connection strings of all three users.</returns>
	public async Task<TestDatabase> CreateDatabaseWithRolesAsync()
	{
		var name = NextName();
		await RunSqlAsync("postgres", $"CREATE DATABASE {name} OWNER {OwnerRole};").ConfigureAwait(false);
		await RunSqlAsync(name, "CREATE EXTENSION postgis;").ConfigureAwait(false);
		await RunSqlAsync(
			name,
			$"REVOKE ALL ON DATABASE {name} FROM PUBLIC; GRANT CONNECT ON DATABASE {name} TO {ApiRole}; GRANT USAGE ON SCHEMA public TO {ApiRole};"
			+ $" ALTER DEFAULT PRIVILEGES FOR ROLE {OwnerRole} IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {ApiRole};"
			+ $" ALTER DEFAULT PRIVILEGES FOR ROLE {OwnerRole} IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO {ApiRole};").ConfigureAwait(false);
		return new TestDatabase(
			name,
			Connect(name, "postgres", "postgres"),
			Connect(name, OwnerRole, ownerPassword),
			Connect(name, ApiRole, apiPassword));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a unique database name for the run.
	/// </summary>
	/// <returns>A name that is a valid unquoted identifier.</returns>
	private string NextName()
	{
		return string.Create(CultureInfo.InvariantCulture, $"parking_test_{Interlocked.Increment(ref databaseCounter)}");
	}

	/// <summary>
	/// Builds the connection string of one user to one database of the container.
	/// </summary>
	/// <param name="database">The database name.</param>
	/// <param name="user">The user name.</param>
	/// <param name="password">The password of the user.</param>
	/// <returns>The connection string.</returns>
	private string Connect(string database, string user, string password)
	{
		var builder = new DbConnectionStringBuilder { ConnectionString = container.GetConnectionString() };
		builder["Database"] = database;
		builder["Username"] = user;
		builder["Password"] = password;
		return builder.ConnectionString;
	}

	/// <summary>
	/// Runs one SQL text as the superuser in the given database and fails the test run when it does not succeed.
	/// </summary>
	/// <param name="database">The database to run in.</param>
	/// <param name="sql">The SQL text.</param>
	/// <returns>A task that completes when the statement has run.</returns>
	/// <exception cref="InvalidOperationException">The statement failed; the message holds the server's answer.</exception>
	private async Task RunSqlAsync(string database, string sql)
	{
		var result = await container.ExecAsync(["psql", "--username", "postgres", "--dbname", database, "--set", "ON_ERROR_STOP=1", "--command", sql]).ConfigureAwait(false);
		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException($"The test server refused the statement: {result.Stderr}");
		}
	}
	#endregion
}
