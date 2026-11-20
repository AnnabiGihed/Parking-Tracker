using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;
using ParkingTracker.Testing.PostgreSql;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Tests.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="ParkingDataUnitOfWork"/> on a real PostGIS server (ADR-0025): a save applies the staged changes,
///              and a unique index that refuses a second account of one subject, or a concurrency token that refuses a second
///              rotation of one refresh token, comes back as a failed result and not as an exception.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class ParkingDataUnitOfWorkTests
{
	#region Static Instances
	/// <summary>
	/// The moment of the sign-in in the tests.
	/// </summary>
	private static readonly DateTimeOffset Start = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingDataUnitOfWorkTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public ParkingDataUnitOfWorkTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Save Tests
	/// <summary>
	/// Verifies that a save applies the staged account and reports success.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_StagedAccount_ShouldPersistItAndSucceed()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using var context = CreateContext(database.ApiConnectionString);
		await new UserAccountRepository(context).AddAsync(UserAccount.Create(Subject('a'), Start));

		var result = await new ParkingDataUnitOfWork(context, NullLogger<ParkingDataUnitOfWork>.Instance).SaveChangesAsync();

		result.IsSuccess.Should().BeTrue();
		await using var read = CreateContext(database.ApiConnectionString);
		(await read.Set<UserAccount>().CountAsync()).Should().Be(1);
	}

	/// <summary>
	/// Verifies that a second account with the same subject hash is a failed result with the database error, not an exception.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_DuplicateSubject_ShouldFailWithTheDatabaseError()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using (var first = CreateContext(database.ApiConnectionString))
		{
			await new UserAccountRepository(first).AddAsync(UserAccount.Create(Subject('a'), Start));
			(await new ParkingDataUnitOfWork(first, NullLogger<ParkingDataUnitOfWork>.Instance).SaveChangesAsync()).IsSuccess.Should().BeTrue();
		}

		await using var second = CreateContext(database.ApiConnectionString);
		await new UserAccountRepository(second).AddAsync(UserAccount.Create(Subject('a'), Start));
		var result = await new ParkingDataUnitOfWork(second, NullLogger<ParkingDataUnitOfWork>.Instance).SaveChangesAsync();

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(ParkingDataUnitOfWork.DatabaseError);
		result.ResultExceptionType.Should().Be(ResultExceptionType.Conflict);
	}

	/// <summary>
	/// Verifies that two requests that rotate the same refresh token cannot both succeed: the second save is a failed result with
	/// the concurrency error.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_SameTokenRotatedTwice_ShouldFailTheSecondWithTheConcurrencyError()
	{
		var database = await CreateMigratedDatabaseAsync();
		UserAccountId id;
		await using (var seed = CreateContext(database.ApiConnectionString))
		{
			var account = UserAccount.Create(Subject('a'), Start);
			_ = account.StartSession(Token('1'), Start);
			id = account.Id;
			await new UserAccountRepository(seed).AddAsync(account);
			_ = await new ParkingDataUnitOfWork(seed, NullLogger<ParkingDataUnitOfWork>.Instance).SaveChangesAsync();
		}

		await using var one = CreateContext(database.ApiConnectionString);
		await using var two = CreateContext(database.ApiConnectionString);
		var accountOne = (await new UserAccountRepository(one).FindByIdAsync(id))!;
		var accountTwo = (await new UserAccountRepository(two).FindByIdAsync(id))!;
		_ = accountOne.Rotate(Token('1'), Token('2'), Start.AddDays(1));
		_ = accountTwo.Rotate(Token('1'), Token('3'), Start.AddDays(1));

		var first = await new ParkingDataUnitOfWork(one, NullLogger<ParkingDataUnitOfWork>.Instance).SaveChangesAsync();
		var second = await new ParkingDataUnitOfWork(two, NullLogger<ParkingDataUnitOfWork>.Instance).SaveChangesAsync();

		first.IsSuccess.Should().BeTrue();
		second.IsFailure.Should().BeTrue();
		second.Error.Should().Be(ParkingDataUnitOfWork.ConcurrencyError);
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
	/// Builds the context the way the host does, on the given connection string.
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
