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
/// Purpose     : Tests for <c>account_profiles</c> and <see cref="AccountProfileRepository"/> on a real PostGIS server (ADR-0025,
///              ADR-0032): the round trip of a profile with its binary payload, the replacement that raises the revision, a stale
///              revision refused by the concurrency token, the foreign key that refuses a profile without an account, the cascade of
///              an account delete, the explicit delete, and the rights of the API role.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class AccountProfileRepositoryTests
{
	#region Static Instances
	/// <summary>
	/// The moment of the sign-in in the tests.
	/// </summary>
	private static readonly DateTimeOffset Start = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// The day of the first write in the tests.
	/// </summary>
	private static readonly DateOnly Today = new(2026, 11, 26);
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccountProfileRepositoryTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public AccountProfileRepositoryTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Round Trip Tests
	/// <summary>
	/// Verifies that a profile comes back as it was saved, bytes included.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Profile_ShouldRoundTrip()
	{
		var database = await CreateMigratedDatabaseAsync();
		var accountId = await SaveAccountAsync(database, 'a');
		var payload = Enumerable.Range(0, 300).Select(value => (byte)value).ToArray();
		await SaveProfileAsync(database, accountId, payload);

		await using var read = CreateContext(database.OwnerConnectionString);
		var loaded = await new AccountProfileRepository(read).FindByIdAsync(accountId);

		loaded.Should().NotBeNull();
		loaded.Id.Should().Be(accountId);
		loaded.Revision.Should().Be(1);
		loaded.SchemaVersion.Should().Be(1);
		loaded.KeyId.Should().Be("0a1b2c3d");
		loaded.Payload.Should().Equal(payload);
		loaded.UpdatedOn.Should().Be(Today);
	}

	/// <summary>
	/// Verifies that a replacement is stored with the next revision and that an unknown account has no profile.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Replace_ShouldStoreTheNextRevision()
	{
		var database = await CreateMigratedDatabaseAsync();
		var accountId = await SaveAccountAsync(database, 'a');
		await SaveProfileAsync(database, accountId, [1, 2, 3]);
		await using (var context = CreateContext(database.OwnerConnectionString))
		{
			var profile = (await new AccountProfileRepository(context).FindByIdAsync(accountId))!;
			profile.Replace(2, EncryptedProfile.Create("ffeeddcc", [9, 9]).Value, Today.AddDays(1)).IsSuccess.Should().BeTrue();
			await context.SaveChangesAsync();
		}

		await using var read = CreateContext(database.OwnerConnectionString);
		var repository = new AccountProfileRepository(read);
		var loaded = (await repository.FindByIdAsync(accountId))!;
		loaded.Revision.Should().Be(2);
		loaded.SchemaVersion.Should().Be(2);
		loaded.KeyId.Should().Be("ffeeddcc");
		loaded.Payload.Should().Equal(9, 9);
		(await repository.FindByIdAsync(UserAccountId.New())).Should().BeNull();
	}
	#endregion

	#region Concurrency Tests
	/// <summary>
	/// Verifies that a write based on a revision that another write replaced meanwhile is refused instead of overwriting it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Replace_StaleRevision_ShouldFailTheSecondSave()
	{
		var database = await CreateMigratedDatabaseAsync();
		var accountId = await SaveAccountAsync(database, 'a');
		await SaveProfileAsync(database, accountId, [1]);
		await using var first = CreateContext(database.OwnerConnectionString);
		await using var second = CreateContext(database.OwnerConnectionString);
		var profileOne = (await new AccountProfileRepository(first).FindByIdAsync(accountId))!;
		var profileTwo = (await new AccountProfileRepository(second).FindByIdAsync(accountId))!;
		profileOne.Replace(1, EncryptedProfile.Create("0a1b2c3d", [2]).Value, Today);
		profileTwo.Replace(1, EncryptedProfile.Create("0a1b2c3d", [3]).Value, Today);
		await first.SaveChangesAsync();

		var act = async () => await second.SaveChangesAsync().ConfigureAwait(false);

		await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
		await using var read = CreateContext(database.OwnerConnectionString);
		(await new AccountProfileRepository(read).FindByIdAsync(accountId))!.Payload.Should().Equal(2);
	}
	#endregion

	#region Constraint Tests
	/// <summary>
	/// Verifies that a profile of an account that does not exist is refused by the foreign key, and that an account has one profile only.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_WithoutAccountOrTwice_ShouldViolateAKey()
	{
		var database = await CreateMigratedDatabaseAsync();
		var accountId = await SaveAccountAsync(database, 'a');
		await SaveProfileAsync(database, accountId, [1]);
		await using var orphan = CreateContext(database.OwnerConnectionString);
		await new AccountProfileRepository(orphan).AddAsync(NewProfile(UserAccountId.New(), [1]));
		await using var twice = CreateContext(database.OwnerConnectionString);
		await new AccountProfileRepository(twice).AddAsync(NewProfile(accountId, [2]));

		var orphanSave = async () => await orphan.SaveChangesAsync().ConfigureAwait(false);
		var twiceSave = async () => await twice.SaveChangesAsync().ConfigureAwait(false);

		(await orphanSave.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
			.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
		(await twiceSave.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
			.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
	}
	#endregion

	#region Delete Tests
	/// <summary>
	/// Verifies that deleting an account removes its profile through the cascade, and leaves the profile of another account.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Delete_Account_ShouldCascadeToItsProfile()
	{
		var database = await CreateMigratedDatabaseAsync();
		var accountId = await SaveAccountAsync(database, 'a');
		var otherId = await SaveAccountAsync(database, 'b');
		await SaveProfileAsync(database, accountId, [1]);
		await SaveProfileAsync(database, otherId, [2]);
		await using (var connection = new NpgsqlConnection(database.OwnerConnectionString))
		{
			await connection.OpenAsync();
			await using var command = new NpgsqlCommand("DELETE FROM user_accounts WHERE \"Id\" = @id", connection);
			command.Parameters.AddWithValue("id", accountId.Value);
			(await command.ExecuteNonQueryAsync()).Should().Be(1);
		}

		(await CountAsync(database.OwnerConnectionString, "account_profiles")).Should().Be(1);
		await using var read = CreateContext(database.OwnerConnectionString);
		(await new AccountProfileRepository(read).FindByIdAsync(otherId)).Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that deleting a profile removes only the profile and not its account.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Delete_Profile_ShouldKeepTheAccount()
	{
		var database = await CreateMigratedDatabaseAsync();
		var accountId = await SaveAccountAsync(database, 'a');
		await SaveProfileAsync(database, accountId, [1]);
		await using (var context = CreateContext(database.OwnerConnectionString))
		{
			var repository = new AccountProfileRepository(context);
			await repository.DeleteAsync((await repository.FindByIdAsync(accountId))!);
			await context.SaveChangesAsync();
		}

		(await CountAsync(database.OwnerConnectionString, "account_profiles")).Should().Be(0);
		(await CountAsync(database.OwnerConnectionString, "user_accounts")).Should().Be(1);
	}
	#endregion

	#region Role Tests
	/// <summary>
	/// Verifies that the API role can select, insert, update and delete in the profile table.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ApiRole_OnTheProfileTable_ShouldSelectInsertUpdateAndDelete()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using var connection = new NpgsqlConnection(database.ApiConnectionString);
		await connection.OpenAsync();
		var statements = new[]
		{
			$"INSERT INTO user_accounts (\"Id\", \"SubjectHash\", \"CreatedOn\", \"LastSeenOn\", \"Version\") VALUES ('{Guid.Empty:D}', '{new string('a', 64)}', '2026-11-26', '2026-11-26', 0)",
			$"INSERT INTO account_profiles (\"Id\", \"Revision\", \"SchemaVersion\", \"KeyId\", \"Payload\", \"UpdatedOn\") VALUES ('{Guid.Empty:D}', 1, 1, '0a1b2c3d', '\\x01', '2026-11-26')",
			"UPDATE account_profiles SET \"Revision\" = 2",
			"SELECT count(*) FROM account_profiles",
			"DELETE FROM account_profiles",
			"DELETE FROM user_accounts",
		};

		foreach (var statement in statements)
		{
			await using var command = new NpgsqlCommand(statement, connection);
			var act = async () => await command.ExecuteNonQueryAsync().ConfigureAwait(false);
			await act.Should().NotThrowAsync($"the API role runs: {statement}");
		}
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
	/// Saves an account.
	/// </summary>
	/// <param name="database">The database.</param>
	/// <param name="subject">The character of the subject hash.</param>
	/// <returns>The identifier of the account.</returns>
	private static async Task<UserAccountId> SaveAccountAsync(TestDatabase database, char subject)
	{
		var account = UserAccount.Create(SubjectHash.Create(new string(subject, 64)).Value, Start);
		using var context = CreateContext(database.OwnerConnectionString);
		await new UserAccountRepository(context).AddAsync(account).ConfigureAwait(false);
		await context.SaveChangesAsync().ConfigureAwait(false);
		return account.Id;
	}

	/// <summary>
	/// Saves the profile of an account at revision 1.
	/// </summary>
	/// <param name="database">The database.</param>
	/// <param name="accountId">The account.</param>
	/// <param name="payload">The encrypted bytes.</param>
	/// <returns>A task that completes when the profile is saved.</returns>
	private static async Task SaveProfileAsync(TestDatabase database, UserAccountId accountId, byte[] payload)
	{
		using var context = CreateContext(database.OwnerConnectionString);
		await new AccountProfileRepository(context).AddAsync(NewProfile(accountId, payload)).ConfigureAwait(false);
		await context.SaveChangesAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Makes a profile at revision 1.
	/// </summary>
	/// <param name="accountId">The account.</param>
	/// <param name="payload">The encrypted bytes.</param>
	/// <returns>The profile.</returns>
	private static AccountProfile NewProfile(UserAccountId accountId, byte[] payload)
	{
		return AccountProfile.Create(accountId, 1, EncryptedProfile.Create("0a1b2c3d", payload).Value, Today).Value;
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
