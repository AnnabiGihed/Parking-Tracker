using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for the erasure log on a real PostGIS server: <see cref="ErasureLog"/> stages one row per subject and moves its
///              day when the subject is erased again, the deletion of an account takes its sessions with it, and the SQL file that
///              pt-erasure-reapply runs after a restore deletes exactly the listed accounts and their sessions, merges and purges the
///              log, and changes nothing when the transaction is rolled back (the dry run).
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class ErasureLogTests
{
	#region Static Instances
	/// <summary>
	/// The moment of the sign-in in the tests.
	/// </summary>
	private static readonly DateTimeOffset Start = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// The day the seeded accounts were created.
	/// </summary>
	private static readonly DateOnly CreatedDay = new(2026, 11, 19);
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ErasureLogTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public ErasureLogTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Record Tests
	/// <summary>
	/// Verifies that a record is only staged until the context saves, that a second record of the same subject moves the day instead
	/// of failing on the key, and that the deletion of an account in the same save takes its sessions with it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Record_TwiceForOneSubject_ShouldKeepOneRowWithTheLaterDay()
	{
		var database = await CreateMigratedDatabaseAsync();
		var subject = Subject('a');
		await using (var write = CreateContext(database.OwnerConnectionString))
		{
			var account = UserAccount.Create(subject, Start);
			_ = account.StartSession(Token('1'), Start);
			await new UserAccountRepository(write).AddAsync(account);
			await new ErasureLog(write).RecordAsync(subject, new DateOnly(2026, 11, 19));
			(await CountLogAsync(database.OwnerConnectionString)).Should().Be(0, "nothing is written before the save");
			_ = await write.SaveChangesAsync();
		}

		await using (var erase = CreateContext(database.OwnerConnectionString))
		{
			var repository = new UserAccountRepository(erase);
			var account = await erase.Set<UserAccount>().Include(candidate => candidate.Sessions).SingleAsync();
			await new ErasureLog(erase).RecordAsync(subject, new DateOnly(2026, 11, 25));
			await repository.DeleteAsync(account);
			_ = await erase.SaveChangesAsync();
		}

		await using var read = CreateContext(database.OwnerConnectionString);
		(await read.Set<UserAccount>().CountAsync()).Should().Be(0);
		(await read.Set<DeviceSession>().CountAsync()).Should().Be(0, "the sessions are deleted with the account");
		(await read.Set<ErasureLogEntry>().SingleAsync()).ErasedOn.Should().Be(new DateOnly(2026, 11, 25));
	}
	#endregion

	#region Reapply Script Tests
	/// <summary>
	/// Verifies that the script deletes the listed accounts (created on or before the erasure day) with their sessions and no other, merges the list into the log (the
	/// later day wins) and purges log rows older than six months.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReapplyScript_Committed_ShouldDeleteOnlyTheListedAccountsAndMergeAndPurgeTheLog()
	{
		var database = await CreateMigratedDatabaseAsync();
		await SeedAsync(database);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var old = today.AddMonths(-7);

		await RunScriptAsync(database, commit: true, (new string('a', 64), CreatedDay), (new string('d', 64), old));

		await using var read = CreateContext(database.OwnerConnectionString);
		var accounts = await read.Set<UserAccount>().Include(account => account.Sessions).ToListAsync();
		accounts.Select(account => account.SubjectHash.Value).Should().Equal(new string('b', 64));
		accounts.Single().Sessions.Should().ContainSingle();
		(await read.Set<DeviceSession>().CountAsync()).Should().Be(1, "the sessions of the deleted account went with it");
		(await read.Set<AccountProfile>().Select(profile => profile.Id).ToListAsync()).Should().BeEquivalentTo(new[] { accounts.Single().Id }, "the profile of the deleted account went with it");
		var log = (await read.Set<ErasureLogEntry>().ToListAsync()).ToDictionary(entry => entry.SubjectHash, entry => entry.ErasedOn, StringComparer.Ordinal);
		log.Keys.Should().BeEquivalentTo(new[] { new string('a', 64), new string('c', 64) }, "the old row is purged, the others kept");
		log[new string('a', 64)].Should().Be(today, "the later day wins");
	}

	/// <summary>
	/// Verifies that the dry run, the same script ended with ROLLBACK, changes nothing at all.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReapplyScript_RolledBack_ShouldChangeNothing()
	{
		var database = await CreateMigratedDatabaseAsync();
		await SeedAsync(database);

		await RunScriptAsync(database, commit: false, (new string('a', 64), CreatedDay));

		await using var read = CreateContext(database.OwnerConnectionString);
		(await read.Set<UserAccount>().CountAsync()).Should().Be(2);
		(await read.Set<DeviceSession>().CountAsync()).Should().Be(2);
		(await read.Set<AccountProfile>().CountAsync()).Should().Be(2);
		(await read.Set<ErasureLogEntry>().CountAsync()).Should().Be(3);
	}

	/// <summary>
	/// Verifies that an account created after the day of its erasure (a new sign-up) survives the script, and one created on or
	/// before that day is deleted.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReapplyScript_ShouldKeepAnAccountCreatedAfterTheErasureDay()
	{
		var database = await CreateMigratedDatabaseAsync();
		await SeedAsync(database);

		await RunScriptAsync(database, commit: true, (new string('a', 64), CreatedDay.AddDays(-1)), (new string('b', 64), CreatedDay));

		await using var read = CreateContext(database.OwnerConnectionString);
		var remaining = await read.Set<UserAccount>().ToListAsync();
		remaining.Select(account => account.SubjectHash.Value).Should().Equal(new[] { new string('a', 64) }, "the account created after the erasure day stays; the one created on that day goes");
	}

	/// <summary>
	/// Verifies that an empty list deletes nothing, because the script then has no subject to match.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReapplyScript_WithAnEmptyList_ShouldDeleteNoAccount()
	{
		var database = await CreateMigratedDatabaseAsync();
		await SeedAsync(database);

		await RunScriptAsync(database, commit: true);

		await using var read = CreateContext(database.OwnerConnectionString);
		(await read.Set<UserAccount>().CountAsync()).Should().Be(2);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Seeds two accounts with one session and one profile each, and a log with a row of the first subject of today, a recent row of another subject and one older than six months.
	/// </summary>
	/// <param name="database">The database.</param>
	/// <returns>A task that completes when the rows are saved.</returns>
	private static async Task SeedAsync(TestDatabase database)
	{
		using var context = CreateContext(database.OwnerConnectionString);
		foreach (var (subject, token) in new[] { ('a', '1'), ('b', '2') })
		{
			var account = UserAccount.Create(Subject(subject), Start);
			_ = account.StartSession(Token(token), Start);
			await new UserAccountRepository(context).AddAsync(account).ConfigureAwait(false);
			await new AccountProfileRepository(context).AddAsync(AccountProfile.Create(account.Id, 1, EncryptedProfile.Create("0a1b2c3d", [1, 2, 3]).Value, DateOnly.FromDateTime(Start.UtcDateTime)).Value).ConfigureAwait(false);
		}

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		_ = context.Set<ErasureLogEntry>().Add(new ErasureLogEntry { SubjectHash = new string('a', 64), ErasedOn = today });
		_ = context.Set<ErasureLogEntry>().Add(new ErasureLogEntry { SubjectHash = new string('c', 64), ErasedOn = today.AddDays(-3) });
		_ = context.Set<ErasureLogEntry>().Add(new ErasureLogEntry { SubjectHash = new string('d', 64), ErasedOn = today.AddMonths(-8) });
		_ = await context.SaveChangesAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Runs the SQL file of the tool the way the tool does: in one transaction, after a temporary table with the listed rows exists,
	/// and ends it with COMMIT or ROLLBACK.
	/// </summary>
	/// <param name="database">The database.</param>
	/// <param name="commit">Whether to commit; otherwise the transaction is rolled back, the dry run.</param>
	/// <param name="rows">The rows of the imported log: subject hash and day.</param>
	/// <returns>A task that completes when the script has run.</returns>
	private static async Task RunScriptAsync(TestDatabase database, bool commit, params (string SubjectHash, DateOnly ErasedOn)[] rows)
	{
		var script = await File.ReadAllTextAsync("erasure-reapply.sql").ConfigureAwait(false);
		using var connection = new NpgsqlConnection(database.OwnerConnectionString);
		await connection.OpenAsync().ConfigureAwait(false);
		using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
		await CreateImportTableAsync(connection, transaction).ConfigureAwait(false);
		await InsertRowsAsync(connection, transaction, rows).ConfigureAwait(false);
		using (var run = new NpgsqlCommand(script, connection, transaction))
		{
			using var reader = await run.ExecuteReaderAsync().ConfigureAwait(false);
			var more = true;
			while (more)
			{
				more = await reader.NextResultAsync().ConfigureAwait(false);
			}
		}

		if (commit)
		{
			await transaction.CommitAsync().ConfigureAwait(false);
		}
		else
		{
			await transaction.RollbackAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Creates the temporary table the tool creates before it runs the SQL file.
	/// </summary>
	/// <param name="connection">The open connection.</param>
	/// <param name="transaction">The transaction the script runs in.</param>
	/// <returns>A task that completes when the table exists.</returns>
	private static async Task CreateImportTableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
	{
		using var create = new NpgsqlCommand("CREATE TEMP TABLE erasure_import (subject_hash text PRIMARY KEY, erased_on date NOT NULL) ON COMMIT DROP", connection, transaction);
		_ = await create.ExecuteNonQueryAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Fills the temporary table with the rows of the exported log.
	/// </summary>
	/// <param name="connection">The open connection.</param>
	/// <param name="transaction">The transaction the script runs in.</param>
	/// <param name="rows">The rows: subject hash and day.</param>
	/// <returns>A task that completes when the rows are inserted.</returns>
	private static async Task InsertRowsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<(string SubjectHash, DateOnly ErasedOn)> rows)
	{
		foreach (var (subjectHash, erasedOn) in rows)
		{
			using var insert = new NpgsqlCommand("INSERT INTO erasure_import VALUES (@hash, @day)", connection, transaction);
			_ = insert.Parameters.AddWithValue("hash", subjectHash);
			_ = insert.Parameters.AddWithValue("day", erasedOn);
			_ = await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Counts the rows of the erasure log.
	/// </summary>
	/// <param name="connectionString">The connection string.</param>
	/// <returns>The number of rows.</returns>
	private static async Task<int> CountLogAsync(string connectionString)
	{
		using var context = CreateContext(connectionString);
		return await context.Set<ErasureLogEntry>().CountAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Creates a migrated database laid out like the server.
	/// </summary>
	/// <returns>The database.</returns>
	private async Task<TestDatabase> CreateMigratedDatabaseAsync()
	{
		var database = await server.CreateDatabaseWithRolesAsync().ConfigureAwait(false);
		using var context = CreateContext(database.OwnerConnectionString);
		await context.Database.MigrateAsync().ConfigureAwait(false);
		return database;
	}

	/// <summary>
	/// Builds a subject hash of one repeated character.
	/// </summary>
	/// <param name="character">The hexadecimal character.</param>
	/// <returns>The subject hash.</returns>
	private static SubjectHash Subject(char character) => SubjectHash.Create(new string(character, 64)).Value;

	/// <summary>
	/// Builds a refresh token hash of one repeated character.
	/// </summary>
	/// <param name="character">The hexadecimal character.</param>
	/// <returns>The refresh token hash.</returns>
	private static RefreshTokenHash Token(char character) => RefreshTokenHash.Create(new string(character, 64)).Value;

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
