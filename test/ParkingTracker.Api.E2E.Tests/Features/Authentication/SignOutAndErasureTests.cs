using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;
using ParkingTracker.Testing.PostgreSql;

namespace ParkingTracker.Api.E2E.Tests.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : End-to-end tests of the sign-out of a device (DELETE /sessions/current) and of the erasure of the account
///              (DELETE /me) on the real API composition and a real PostGIS server: only the family of the access token is revoked,
///              a token without a session claim or for another account's family changes nothing, both calls are idempotent, the
///              erasure removes the account with all its sessions and writes the erasure log in one transaction (and rolls back
///              with it), the old access token stops working, and the same Google account signs in again as a new account.
/// </summary>
[Collection(PostgisServerGroup.Name)]
public sealed class SignOutAndErasureTests
{
	#region Constants
	/// <summary>
	/// The Google subject the tests sign in with.
	/// </summary>
	private const string Subject = "google-subject-erase";

	/// <summary>
	/// The Google subject of a second user.
	/// </summary>
	private const string OtherSubject = "google-subject-bystander";
	#endregion

	#region Fields
	/// <summary>
	/// The shared server.
	/// </summary>
	private readonly PostgisServer server;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SignOutAndErasureTests"/> class.
	/// </summary>
	/// <param name="server">The shared server.</param>
	public SignOutAndErasureTests(PostgisServer server)
	{
		this.server = server;
	}
	#endregion

	#region Sign Out Tests
	/// <summary>
	/// Verifies that the sign-out answers 204, cuts the refresh of that device at once, leaves the other device working, and that
	/// the access token already issued stays valid until it expires while the account reports one device less.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOut_ShouldRevokeOnlyTheFamilyOfTheCaller()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		var otherDevice = await harness.SignInAsync(Subject);

		var response = await harness.SignOutAsync(device.AccessToken);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await harness.RefreshAsync(device.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await harness.RefreshAsync(otherDevice.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
		var me = await harness.GetMeAsync(device.AccessToken);
		me.StatusCode.Should().Be(HttpStatusCode.OK, "an issued access token stays valid until it expires");
		(await me.Content.ReadAsStringAsync()).Should().Contain("\"activeSessionCount\":1");
	}

	/// <summary>
	/// Verifies that signing out twice with the same access token answers 204 both times.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOut_Twice_ShouldBeIdempotent()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);

		var first = await harness.SignOutAsync(device.AccessToken);
		var second = await harness.SignOutAsync(device.AccessToken);

		first.StatusCode.Should().Be(HttpStatusCode.NoContent);
		second.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	/// <summary>
	/// Verifies that without an access token, or with one that names no session, the sign-out is 401 and changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOut_WithoutTokenOrWithoutSessionClaim_ShouldAnswer401AndChangeNothing()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		using var key = AccessTokenForger.ReadKey(harness.Factory.Secrets.CurrentKeyPem);
		var withoutSid = AccessTokenForger.Create(key, await AccountIdAsync(harness));

		var anonymous = await harness.SignOutAsync(null);
		var noSession = await harness.SignOutAsync(withoutSid);

		anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		noSession.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await harness.RefreshAsync(device.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// Verifies that a token whose session claim names the family of another account, or none at all, answers 204 and revokes
	/// nothing: an account can only end its own families.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOut_WithTheFamilyOfAnotherAccountOrAnUnknownOne_ShouldRevokeNothing()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var mine = await harness.SignInAsync(Subject);
		var bystander = await harness.SignInAsync(OtherSubject);
		using var key = AccessTokenForger.ReadKey(harness.Factory.Secrets.CurrentKeyPem);
		var account = await AccountIdAsync(harness, Subject);
		var foreignFamily = await FamilyIdAsync(harness, OtherSubject);

		var foreign = await harness.SignOutAsync(AccessTokenForger.Create(key, account, familyId: foreignFamily));
		var unknown = await harness.SignOutAsync(AccessTokenForger.Create(key, account, familyId: Guid.NewGuid()));

		foreign.StatusCode.Should().Be(HttpStatusCode.NoContent);
		unknown.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await harness.RefreshAsync(bystander.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
		(await harness.RefreshAsync(mine.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// Verifies that the sign-out of a device whose account was erased answers 204.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SignOut_AfterTheAccountWasErased_ShouldAnswer204()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		(await harness.DeleteAccountAsync(device.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var response = await harness.SignOutAsync(device.AccessToken);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}
	#endregion

	#region Erasure Tests
	/// <summary>
	/// Verifies that the erasure answers 204, removes the account and its sessions, writes the erasure log with the keyed hash of
	/// the subject and today, and leaves the account of another user alone.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_ShouldDeleteTheAccountAndItsSessionsAndLogTheSubjectHash()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		_ = await harness.SignInAsync(OtherSubject);

		var response = await harness.DeleteAccountAsync(device.AccessToken);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		await using var context = harness.OpenContext();
		var remaining = await context.Set<UserAccount>().Include(account => account.Sessions).SingleAsync();
		remaining.SubjectHash.Value.Should().Be(HashSubject(harness.Factory.Secrets.SubjectHashKey, OtherSubject));
		(await context.Set<DeviceSession>().CountAsync()).Should().Be(1, "the sessions of the erased account went with it");
		var entry = await context.Set<ErasureLogEntry>().SingleAsync();
		entry.SubjectHash.Should().Be(HashSubject(harness.Factory.Secrets.SubjectHashKey, Subject)).And.NotContain(Subject);
		entry.ErasedOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
	}

	/// <summary>
	/// Verifies that an account with two devices is erased with both families, and that neither refresh token works afterwards.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_WithTwoFamilies_ShouldRemoveBoth()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var first = await harness.SignInAsync(Subject);
		var second = await harness.SignInAsync(Subject);
		_ = await harness.RefreshSucceedingAsync(second.RefreshToken);

		var response = await harness.DeleteAccountAsync(first.AccessToken);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		await using var context = harness.OpenContext();
		(await context.Set<UserAccount>().CountAsync()).Should().Be(0);
		(await context.Set<DeviceSession>().CountAsync()).Should().Be(0);
		(await harness.RefreshAsync(first.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await harness.RefreshAsync(second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	/// <summary>
	/// Verifies that the old access token of an erased account is refused by the account read, and that a second erasure with that
	/// token answers 204 without error.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_OldAccessToken_ShouldBeRefusedByMeAndErasureShouldBeIdempotent()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		(await harness.DeleteAccountAsync(device.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var me = await harness.GetMeAsync(device.AccessToken);
		var again = await harness.DeleteAccountAsync(device.AccessToken);

		me.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the account of the token no longer exists");
		again.StatusCode.Should().Be(HttpStatusCode.NoContent);
		await using var context = harness.OpenContext();
		(await context.Set<ErasureLogEntry>().CountAsync()).Should().Be(1);
	}

	/// <summary>
	/// Verifies that the erasure is not a ban: the same Google account signs in again, gets a new account, and erasing that one
	/// as well keeps one row in the erasure log.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_ThenSignInAgain_ShouldCreateANewAccountAndKeepOneLogRow()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var first = await harness.SignInAsync(Subject);
		var firstId = await AccountIdAsync(harness);
		(await harness.DeleteAccountAsync(first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var second = await harness.SignInAsync(Subject);

		var secondId = await AccountIdAsync(harness);
		secondId.Should().NotBe(firstId);
		(await harness.GetMeAsync(second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
		(await harness.DeleteAccountAsync(second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		await using var context = harness.OpenContext();
		(await context.Set<ErasureLogEntry>().CountAsync()).Should().Be(1);
	}

	/// <summary>
	/// Verifies that two erasures of one account at the same moment, both having read the account before either saves, both answer
	/// 204: the one that loses the save finds the account gone, and the log keeps one row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_TwoRequestsAtOnce_ShouldBothAnswer204()
	{
		using var rendezvous = new Barrier(2);
		await using var harness = await SignInHarness.StartAsync(server, configureServices: services => RendezvousRepository.Install(services, rendezvous, RendezvousRepository.HeldRead.ById));
		var device = await harness.SignInAsync(Subject);

		var responses = await Task.WhenAll(harness.DeleteAccountAsync(device.AccessToken), harness.DeleteAccountAsync(device.AccessToken));

		responses.Select(response => response.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.NoContent);
		await using var context = harness.OpenContext();
		(await context.Set<UserAccount>().CountAsync()).Should().Be(0);
		(await context.Set<ErasureLogEntry>().CountAsync()).Should().Be(1);
	}

	/// <summary>
	/// Verifies that the erasure without an access token is 401 and deletes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_WithoutAccessToken_ShouldAnswer401AndDeleteNothing()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		_ = await harness.SignInAsync(Subject);

		var response = await harness.DeleteAccountAsync(null);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		await using var context = harness.OpenContext();
		(await context.Set<UserAccount>().CountAsync()).Should().Be(1);
		(await context.Set<ErasureLogEntry>().CountAsync()).Should().Be(0);
	}

	/// <summary>
	/// Verifies that the deletion and the log record are one transaction: when the log cannot be written, the account stays, the
	/// answer is not a success, and the session still works.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_WhenTheLogCannotBeWritten_ShouldKeepTheAccount()
	{
		await using var harness = await SignInHarness.StartAsync(server);
		var device = await harness.SignInAsync(Subject);
		await using (var owner = harness.OpenContext())
		{
			_ = await owner.Database.ExecuteSqlRawAsync("REVOKE INSERT, UPDATE ON erasure_log FROM pt_api");
		}

		var response = await harness.DeleteAccountAsync(device.AccessToken);

		response.StatusCode.Should().NotBe(HttpStatusCode.NoContent);
		response.IsSuccessStatusCode.Should().BeFalse();
		await using var context = harness.OpenContext();
		(await context.Set<UserAccount>().CountAsync()).Should().Be(1, "the deletion rolled back with the failed log record");
		(await context.Set<DeviceSession>().CountAsync()).Should().Be(1);
		(await harness.GetMeAsync(device.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the identifier of the account of a subject, as the string the sub claim carries.
	/// </summary>
	/// <param name="harness">The harness.</param>
	/// <param name="subject">The Google subject; the default one when omitted.</param>
	/// <returns>The account identifier.</returns>
	private static async Task<string> AccountIdAsync(SignInHarness harness, string subject = Subject)
	{
		using var context = harness.OpenContext();
		var hash = HashSubject(harness.Factory.Secrets.SubjectHashKey, subject);
		var accounts = await context.Set<UserAccount>().ToListAsync().ConfigureAwait(false);
		return accounts.Single(account => string.Equals(account.SubjectHash.Value, hash, StringComparison.Ordinal)).Id.Value.ToString("D");
	}

	/// <summary>
	/// Reads the family of the only session of the account of a subject.
	/// </summary>
	/// <param name="harness">The harness.</param>
	/// <param name="subject">The Google subject.</param>
	/// <returns>The family.</returns>
	private static async Task<Guid> FamilyIdAsync(SignInHarness harness, string subject)
	{
		using var context = harness.OpenContext();
		var hash = HashSubject(harness.Factory.Secrets.SubjectHashKey, subject);
		var accounts = await context.Set<UserAccount>().Include(account => account.Sessions).ToListAsync().ConfigureAwait(false);
		return accounts.Single(account => string.Equals(account.SubjectHash.Value, hash, StringComparison.Ordinal)).Sessions.Single().FamilyId;
	}

	/// <summary>
	/// Computes the keyed hash of a subject as the API must: HMAC-SHA256 in lowercase hexadecimal.
	/// </summary>
	/// <param name="base64Key">The subject hash key as base64.</param>
	/// <param name="subject">The subject.</param>
	/// <returns>The hash.</returns>
	private static string HashSubject(string base64Key, string subject)
	{
		return Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromBase64String(base64Key), Encoding.UTF8.GetBytes(subject)));
	}
	#endregion
}
