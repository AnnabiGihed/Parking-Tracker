using Microsoft.EntityFrameworkCore;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Shared.Database;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="SignInChallengeStore"/> on a real PostGIS server (ADR-0025), as the less-privileged role the API
///              serves with: a nonce is consumed once, an unknown, used or expired one is not, twenty requests racing for one nonce
///              give exactly one winner, a new nonce purges the expired ones, and a nonce full of SQL stays text.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class SignInChallengeStoreTests
{
	#region Static Instances
	/// <summary>
	/// The moment of the tests.
	/// </summary>
	private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SignInChallengeStoreTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public SignInChallengeStoreTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Consume Tests
	/// <summary>
	/// Verifies that a stored nonce is consumed once and refused the second time.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Consume_StoredNonce_ShouldSucceedOnceOnly()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using var context = CreateContext(database.ApiConnectionString);
		var store = new SignInChallengeStore(context);
		await store.CreateAsync("n1", Now.AddMinutes(5), Now);

		var first = await store.ConsumeAsync("n1", Now.AddMinutes(1));
		var second = await store.ConsumeAsync("n1", Now.AddMinutes(1));

		first.Should().BeTrue();
		second.Should().BeFalse("a nonce is single use");
	}

	/// <summary>
	/// Verifies that an unknown nonce is refused, and that a nonce is refused at and after its expiry but accepted just before.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Consume_UnknownOrExpiredNonce_ShouldBeRefused()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using var context = CreateContext(database.ApiConnectionString);
		var store = new SignInChallengeStore(context);
		var expiry = Now.AddMinutes(5);
		await store.CreateAsync("at-expiry", expiry, Now);
		await store.CreateAsync("after-expiry", expiry, Now);
		await store.CreateAsync("before-expiry", expiry, Now);

		(await store.ConsumeAsync("never-issued", Now)).Should().BeFalse();
		(await store.ConsumeAsync("at-expiry", expiry)).Should().BeFalse("the nonce is valid until its expiry, not at it");
		(await store.ConsumeAsync("after-expiry", expiry.AddSeconds(1))).Should().BeFalse();
		(await store.ConsumeAsync("before-expiry", expiry.AddTicks(-1))).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that twenty requests that race for one nonce, each on a connection of its own, give exactly one winner.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Consume_TwentyRacingRequests_ShouldGiveExactlyOneWinner()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using (var seed = CreateContext(database.ApiConnectionString))
		{
			await new SignInChallengeStore(seed).CreateAsync("contested", Now.AddMinutes(5), Now);
		}

		var attempts = Enumerable.Range(0, 20).Select(async _ =>
		{
			using var context = CreateContext(database.ApiConnectionString);
			return await new SignInChallengeStore(context).ConsumeAsync("contested", Now.AddMinutes(1)).ConfigureAwait(false);
		});
		var results = await Task.WhenAll(attempts);

		results.Count(won => won).Should().Be(1);
	}

	/// <summary>
	/// Verifies that a nonce full of SQL is stored and consumed as plain text and leaves the table alone.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Create_NonceWithSql_ShouldStayText()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using var context = CreateContext(database.ApiConnectionString);
		var store = new SignInChallengeStore(context);
		const string Hostile = "'; DROP TABLE sign_in_challenges; --";

		await store.CreateAsync(Hostile, Now.AddMinutes(5), Now);

		(await store.ConsumeAsync(Hostile, Now)).Should().BeTrue();
		(await context.Set<SignInChallenge>().CountAsync()).Should().Be(0);
	}
	#endregion

	#region Create Tests
	/// <summary>
	/// Verifies that storing a nonce removes the rows that expired before the given moment and keeps the others.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Create_ShouldPurgeExpiredRowsOnly()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using var context = CreateContext(database.ApiConnectionString);
		var store = new SignInChallengeStore(context);
		await store.CreateAsync("old", Now.AddMinutes(-10).AddMinutes(5), Now.AddMinutes(-10));
		await store.CreateAsync("live", Now.AddMinutes(3), Now.AddMinutes(-2));

		await store.CreateAsync("new", Now.AddMinutes(5), Now);

		(await context.Set<SignInChallenge>().Select(challenge => challenge.Nonce).ToListAsync()).Should().BeEquivalentTo("live", "new");
	}

	/// <summary>
	/// Verifies that a blank nonce is refused before the database is touched, and a missing one on consumption too.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Create_BlankNonce_ShouldThrow()
	{
		var database = await CreateMigratedDatabaseAsync();
		await using var context = CreateContext(database.ApiConnectionString);
		var store = new SignInChallengeStore(context);

		var create = async () => await store.CreateAsync(" ", Now.AddMinutes(5), Now).ConfigureAwait(false);
		var consume = async () => await store.ConsumeAsync(null!, Now).ConfigureAwait(false); // null on purpose: proves the guard clause rejects it

		await create.Should().ThrowAsync<ArgumentException>();
		await consume.Should().ThrowAsync<ArgumentNullException>();
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
