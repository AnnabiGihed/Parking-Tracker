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
/// Purpose     : Tests for the account tables and <see cref="UserAccountRepository"/> on a real PostGIS server (ADR-0025): the round
///              trip of an account with its sessions, the unique subject and token hashes, the lookup by token, a family revoked
///              after a reuse, the hard delete, a second rotation of the same token refused by the concurrency token, the single-use
///              nonce statement, and the rights of the API role on the four tables.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class UserAccountRepositoryTests
{
	#region Static Instances
	/// <summary>
	/// The moment of the sign-in in the tests.
	/// </summary>
	private static readonly DateTimeOffset Start = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="UserAccountRepositoryTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public UserAccountRepositoryTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Round Trip Tests
	/// <summary>
	/// Verifies that an account with a rotated chain of sessions comes back as it was saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Account_WithSessions_ShouldRoundTrip()
	{
		var database = await CreateMigratedDatabaseAsync();
		var account = UserAccount.Create(Subject('a'), Start);
		account.StartSession(Token('1'), Start);
		account.Rotate(Token('1'), Token('2'), Start.AddDays(2));
		await using (var write = CreateContext(database.OwnerConnectionString))
		{
			await new UserAccountRepository(write).AddAsync(account);
			await write.SaveChangesAsync();
		}

		await using var read = CreateContext(database.OwnerConnectionString);
		var loaded = await new UserAccountRepository(read).FindByIdAsync(account.Id);

		loaded.Should().NotBeNull();
		loaded.SubjectHash.Should().Be(account.SubjectHash);
		loaded.CreatedOn.Should().Be(new DateOnly(2026, 11, 19));
		loaded.LastSeenOn.Should().Be(new DateOnly(2026, 11, 21));
		loaded.Sessions.Should().HaveCount(2);
		var first = loaded.Sessions.Single(session => session.TokenHash == Token('1'));
		first.RotatedOn.Should().Be(Start.AddDays(2));
		first.AbsoluteExpiryUtc.Should().Be(Start.AddDays(UserAccount.AbsoluteLifetimeDays));
		first.LastUsedOn.Should().Be(new DateOnly(2026, 11, 19));
		loaded.Sessions.Single(session => session.TokenHash == Token('2')).FamilyId.Should().Be(first.FamilyId);
	}

	/// <summary>
	/// Verifies that a session added later to a loaded account is inserted and the rotation of the old one is updated.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Rotate_OnLoadedAccount_ShouldInsertTheNewSessionAndUpdateTheOld()
	{
		var database = await CreateMigratedDatabaseAsync();
		var id = await SaveNewAccountAsync(database, 'a', '1');
		await using var context = CreateContext(database.OwnerConnectionString);
		var repository = new UserAccountRepository(context);
		var account = (await repository.FindByIdAsync(id))!;

		account.Rotate(Token('1'), Token('2'), Start.AddDays(1)).IsSuccess.Should().BeTrue();
		await context.SaveChangesAsync();

		await using var read = CreateContext(database.OwnerConnectionString);
		var loaded = (await new UserAccountRepository(read).FindByIdAsync(id))!;
		loaded.Sessions.Should().HaveCount(2);
		loaded.Sessions.Count(session => session.RotatedOn is not null).Should().Be(1);
	}
	#endregion

	#region Lookup Tests
	/// <summary>
	/// Verifies that the account is found by its subject hash and by any refresh token hash of its sessions, and that unknown
	/// hashes find nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Find_BySubjectAndByToken_ShouldReturnTheAccountWithItsSessions()
	{
		var database = await CreateMigratedDatabaseAsync();
		var id = await SaveNewAccountAsync(database, 'a', '1');
		await SaveNewAccountAsync(database, 'b', '2');
		await using var context = CreateContext(database.OwnerConnectionString);
		var repository = new UserAccountRepository(context);

		var bySubject = await repository.FindBySubjectHashAsync(Subject('a'));
		var byToken = await repository.FindByRefreshTokenHashAsync(Token('1'));
		var unknownSubject = await repository.FindBySubjectHashAsync(Subject('f'));
		var unknownToken = await repository.FindByRefreshTokenHashAsync(Token('f'));

		bySubject!.Id.Should().Be(id);
		bySubject.Sessions.Should().ContainSingle();
		byToken!.Id.Should().Be(id);
		unknownSubject.Should().BeNull();
		unknownToken.Should().BeNull();
	}
	#endregion

	#region Constraint Tests
	/// <summary>
	/// Verifies that two accounts cannot share a subject hash.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_SecondAccountWithSameSubject_ShouldViolateTheUniqueIndex()
	{
		var database = await CreateMigratedDatabaseAsync();
		await SaveNewAccountAsync(database, 'a', '1');
		await using var context = CreateContext(database.OwnerConnectionString);
		await new UserAccountRepository(context).AddAsync(UserAccount.Create(Subject('a'), Start));

		var act = async () => await context.SaveChangesAsync().ConfigureAwait(false);

		(await act.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
			.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
	}

	/// <summary>
	/// Verifies that two sessions cannot share a token hash, even on different accounts.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_SessionWithSameTokenOnAnotherAccount_ShouldViolateTheUniqueIndex()
	{
		var database = await CreateMigratedDatabaseAsync();
		await SaveNewAccountAsync(database, 'a', '1');
		await using var context = CreateContext(database.OwnerConnectionString);
		var other = UserAccount.Create(Subject('b'), Start);
		other.StartSession(Token('1'), Start);
		await new UserAccountRepository(context).AddAsync(other);

		var act = async () => await context.SaveChangesAsync().ConfigureAwait(false);

		(await act.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
			.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
	}
	#endregion

	#region Revocation Tests
	/// <summary>
	/// Verifies that the revocation of a family after the reuse of a token is saved, and that another family stays active.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Rotate_ReuseOfOldToken_ShouldPersistTheRevokedFamily()
	{
		var database = await CreateMigratedDatabaseAsync();
		var account = UserAccount.Create(Subject('a'), Start);
		account.StartSession(Token('1'), Start);
		account.StartSession(Token('7'), Start);
		account.Rotate(Token('1'), Token('2'), Start.AddDays(1));
		await using (var write = CreateContext(database.OwnerConnectionString))
		{
			await new UserAccountRepository(write).AddAsync(account);
			await write.SaveChangesAsync();
		}

		await using (var context = CreateContext(database.OwnerConnectionString))
		{
			var loaded = (await new UserAccountRepository(context).FindByRefreshTokenHashAsync(Token('1')))!;
			loaded.Rotate(Token('1'), Token('3'), Start.AddDays(2)).IsFailure.Should().BeTrue();
			await context.SaveChangesAsync();
		}

		await using var read = CreateContext(database.OwnerConnectionString);
		var result = (await new UserAccountRepository(read).FindByIdAsync(account.Id))!;
		result.Sessions.Should().HaveCount(3);
		result.Sessions.Where(session => session.TokenHash != Token('7')).Should().OnlyContain(session => session.Revoked);
		result.Sessions.Single(session => session.TokenHash == Token('7')).Revoked.Should().BeFalse();
		result.ActiveSessionCount(Start.AddDays(2)).Should().Be(1);
	}

	/// <summary>
	/// Verifies that when two requests rotate the same token, the second save fails instead of issuing two successors.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Rotate_SameTokenInTwoContexts_ShouldFailTheSecondSave()
	{
		var database = await CreateMigratedDatabaseAsync();
		var id = await SaveNewAccountAsync(database, 'a', '1');
		await using var first = CreateContext(database.OwnerConnectionString);
		await using var second = CreateContext(database.OwnerConnectionString);
		var accountOne = (await new UserAccountRepository(first).FindByIdAsync(id))!;
		var accountTwo = (await new UserAccountRepository(second).FindByIdAsync(id))!;
		accountOne.Rotate(Token('1'), Token('2'), Start.AddDays(1));
		accountTwo.Rotate(Token('1'), Token('3'), Start.AddDays(1));
		await first.SaveChangesAsync();

		var act = async () => await second.SaveChangesAsync().ConfigureAwait(false);

		await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
	}
	#endregion

	#region Delete Tests
	/// <summary>
	/// Verifies that deleting an account removes its sessions too.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Delete_Account_ShouldRemoveItsSessions()
	{
		var database = await CreateMigratedDatabaseAsync();
		var id = await SaveNewAccountAsync(database, 'a', '1');
		await SaveNewAccountAsync(database, 'b', '2');
		await using (var context = CreateContext(database.OwnerConnectionString))
		{
			var repository = new UserAccountRepository(context);
			await repository.DeleteAsync((await repository.FindByIdAsync(id))!);
			await context.SaveChangesAsync();
		}

		(await CountAsync(database.OwnerConnectionString, "user_accounts")).Should().Be(1);
		(await CountAsync(database.OwnerConnectionString, "device_sessions")).Should().Be(1);
	}
	#endregion

	#region Role Tests
	/// <summary>
	/// Verifies that the API role can select, insert, update and delete in the four tables, and that a nonce is consumed once by a
	/// single DELETE ... RETURNING that also leaves expired rows to a purge.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApiRole_OnTheFourTables_ShouldSelectInsertUpdateAndDelete()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using var connection = new NpgsqlConnection(database.ApiConnectionString);
		await connection.OpenAsync();
		var subject = new string('a', 64);
		var token = new string('1', 64);
		var statements = new[]
		{
			$"INSERT INTO user_accounts (\"Id\", \"SubjectHash\", \"CreatedOn\", \"LastSeenOn\", \"Version\") VALUES ('{Guid.Empty:D}', '{subject}', '2026-11-19', '2026-11-19', 0)",
			$"INSERT INTO device_sessions (\"Id\", \"FamilyId\", \"TokenHash\", \"LastUsedOn\", \"AbsoluteExpiryUtc\", \"Revoked\", \"AccountId\") VALUES ('{Guid.Empty:D}', '{Guid.Empty:D}', '{token}', '2026-11-19', now(), false, '{Guid.Empty:D}')",
			"INSERT INTO sign_in_challenges (\"Nonce\", \"ExpiresAtUtc\") VALUES ('n1', now() + interval '5 minutes')",
			$"INSERT INTO erasure_log (\"SubjectHash\", \"ErasedOn\") VALUES ('{subject}', '2026-11-19')",
			"UPDATE user_accounts SET \"LastSeenOn\" = '2026-11-20'",
			"UPDATE device_sessions SET \"Revoked\" = true",
			"UPDATE erasure_log SET \"ErasedOn\" = '2026-11-20'",
			"SELECT count(*) FROM user_accounts",
			"SELECT count(*) FROM device_sessions",
			"SELECT count(*) FROM erasure_log",
			"DELETE FROM erasure_log",
			"DELETE FROM device_sessions",
			"DELETE FROM user_accounts",
		};

		foreach (var statement in statements)
		{
			await using var command = new NpgsqlCommand(statement, connection);
			var act = async () => await command.ExecuteNonQueryAsync().ConfigureAwait(false);
			await act.Should().NotThrowAsync($"the API role runs: {statement}");
		}

		await using var consume = new NpgsqlCommand("DELETE FROM sign_in_challenges WHERE \"Nonce\" = @nonce AND \"ExpiresAtUtc\" > now() RETURNING \"Nonce\"", connection);
		consume.Parameters.AddWithValue("nonce", "n1");
		(await consume.ExecuteScalarAsync()).Should().Be("n1");
		(await consume.ExecuteScalarAsync()).Should().BeNull("a nonce is single use");
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Makes a database laid out like the server and migrates it as the owner role.
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
	/// Saves an account with one session.
	/// </summary>
	/// <param name="database">The database.</param>
	/// <param name="subject">The character of the subject hash.</param>
	/// <param name="token">The character of the token hash.</param>
	/// <returns>The identifier of the account.</returns>
	private static async Task<UserAccountId> SaveNewAccountAsync(TestDatabase database, char subject, char token)
	{
		var account = UserAccount.Create(Subject(subject), Start);
		account.StartSession(Token(token), Start);
		using var context = CreateContext(database.OwnerConnectionString);
		await new UserAccountRepository(context).AddAsync(account).ConfigureAwait(false);
		await context.SaveChangesAsync().ConfigureAwait(false);
		return account.Id;
	}

	/// <summary>
	/// Counts the rows of a table.
	/// </summary>
	/// <param name="connectionString">The connection string.</param>
	/// <param name="table">The table name.</param>
	/// <returns>The number of rows.</returns>
	private static async Task<long> CountAsync(string connectionString, string table)
	{
		using var connection = new NpgsqlConnection(connectionString);
		await connection.OpenAsync().ConfigureAwait(false);
		using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
		return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
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
