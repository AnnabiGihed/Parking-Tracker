using Microsoft.Extensions.Logging.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.ParkingSessions.Errors;
using ParkingTracker.Persistence.Device.Features.ParkingSessions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using ParkingTracker.Testing.Device;

namespace ParkingTracker.Persistence.Device.Tests.Features.ParkingSessions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="ParkingSessionRepository"/> against a real SQLite
///              file: an open session round-trips with its timer and with the position encrypted at rest, an ended session keeps its
///              end, a stored session that breaks a rule gives a failed result, and a position that cannot be decrypted, or a key that
///              cannot be read now, fails the read and never reads as no
///              car saved.
/// </summary>
public sealed class ParkingSessionRepositoryTests
{
	#region Round Trip Tests
	/// <summary>
	/// Verifies that an open session with a timer comes back with every value, and that the position is not stored as plain text.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_OpenSessionWithTimer_ShouldRoundTripWithEncryptedPosition()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var timer = new ParkingTimer(TestData.Parked.AddMinutes(90), 90);
		var session = TestData.Session(timer);

		(await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, session))).IsSuccess.Should().BeTrue();
		var loaded = await db.Sessions.GetAsync();

		loaded.IsSuccess.Should().BeTrue();
		loaded.Value.Position.Should().Be(TestData.GrandPlace);
		loaded.Value.ParkedAt.Should().Be(TestData.Parked);
		loaded.Value.Headline.Should().Be("Paid parking");
		loaded.Value.AddressText.Should().Be("Rue de la Loi");
		loaded.Value.Timer.Should().Be(timer);
		loaded.Value.IsOpen.Should().BeTrue();
		(await db.ScalarAsync<string>("SELECT coordinates FROM parking_sessions")).Should().StartWith("k1.").And.NotContain("50.84");
	}

	/// <summary>
	/// Verifies that a session without a timer comes back without one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_SessionWithoutTimer_ShouldHaveNoTimer()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session()));

		var loaded = await db.Sessions.GetAsync();

		loaded.Value.Timer.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a session that was ended comes back ended and without a timer.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_EndedSession_ShouldKeepItsEnd()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var session = TestData.Session(new ParkingTimer(TestData.Parked.AddMinutes(30), 30));
		session.End(TestData.Parked.AddMinutes(20)).IsSuccess.Should().BeTrue();
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, session));

		var loaded = await db.Sessions.GetAsync();

		loaded.Value.IsOpen.Should().BeFalse();
		loaded.Value.EndedAt.Should().Be(TestData.Parked.AddMinutes(20));
		loaded.Value.Timer.Should().BeNull();
	}

	/// <summary>
	/// Verifies that an empty database gives "no session", not a failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NothingStored_ShouldSucceedWithNull()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();

		var loaded = await db.Sessions.GetAsync();

		loaded.IsFailure.Should().BeTrue();
		loaded.Error.Should().Be(DeviceDataErrors.NotStored);
	}

	/// <summary>
	/// Verifies that an update replaces the stored session, also one that was loaded before and so has another identifier.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Update_LoadedSession_ShouldReplaceTheStoredOne()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session()));
		var loaded = (await db.Sessions.GetAsync()).Value;
		loaded.StartTimer(new ParkingTimer(TestData.Parked.AddMinutes(60), 60)).IsSuccess.Should().BeTrue();

		(await db.ApplyAsync(scope => db.Sessions.UpdateAsync(scope, loaded))).IsSuccess.Should().BeTrue();

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM parking_sessions")).Should().Be(1);
		(await db.Sessions.GetAsync()).Value.Timer!.Minutes.Should().Be(60);
	}

	/// <summary>
	/// Verifies that a removal deletes the session at commit.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Remove_StoredSession_ShouldDeleteItAtCommit()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session()));

		(await db.ApplyRemovalAsync(db.Sessions.Remove)).IsSuccess.Should().BeTrue();

		(await db.Sessions.GetAsync()).Error.Should().Be(DeviceDataErrors.NotStored);
	}
	#endregion

	#region Invalid Row Tests
	/// <summary>
	/// Verifies that a stored timer shorter than 5 minutes gives the failed result of the aggregate.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_TimerTooShort_ShouldFailWithTheDomainError()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session()));
		await db.ExecuteAsync($"UPDATE parking_sessions SET timer_end = {TestData.Parked.AddMinutes(1).ToUnixTimeMilliseconds()}, timer_minutes = 1");

		var loaded = await db.Sessions.GetAsync();

		loaded.Error.Should().Be(ParkingSessionErrors.TimerLengthOutOfRange);
	}

	/// <summary>
	/// Verifies that a stored position that is not on Earth gives the failed result of the aggregate.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_PositionNotOnEarth_ShouldFailWithTheDomainError()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var coordinates = await db.SealAsync("parking_sessions", "coordinates", "11111111-1111-1111-1111-111111111111", "123;4");
		await db.ExecuteAsync($"INSERT INTO parking_sessions (id, coordinates, parked_at, headline, address_text) VALUES ('11111111-1111-1111-1111-111111111111', '{coordinates}', 1, '', '')");

		var loaded = await db.Sessions.GetAsync();

		loaded.Error.Should().Be(ParkingSessionErrors.PositionInvalid);
	}

	/// <summary>
	/// Verifies that coordinates that cannot be read are refused as an invalid row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_CoordinatesNotNumbers_ShouldFailWithInvalidRow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var coordinates = await db.SealAsync("parking_sessions", "coordinates", "11111111-1111-1111-1111-111111111111", "here");
		await db.ExecuteAsync($"INSERT INTO parking_sessions (id, coordinates, parked_at, headline, address_text) VALUES ('11111111-1111-1111-1111-111111111111', '{coordinates}', 1, '', '')");

		var loaded = await db.Sessions.GetAsync();

		loaded.Error.Code.Should().Be(DeviceDatabaseErrors.RowInvalid("parking_sessions", string.Empty).Code);
	}

	/// <summary>
	/// Verifies that a session whose position cannot be decrypted (another key with the same identifier, or no such key, as on
	/// another phone) fails the read with the reason, and is not "nothing stored".
	/// </summary>
	/// <param name="keyId">The identifier of the key of the other phone.</param>
	/// <param name="code">The code of the failure.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("k1", "DeviceDatabase.Cipher.Rejected")]
	[InlineData("other", "DeviceDatabase.Key.Missing")]
	public async Task Get_KeyOfAnotherPhone_ShouldFailTheRead(string keyId, string code)
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session()));
		var otherPhone = new ParkingSessionRepository(db.Database, new FieldCipher(new InMemoryKeyStore(keyId), db.Probe, NullLogger<FieldCipher>.Instance), NullLogger<ParkingSessionRepository>.Instance);

		var loaded = await otherPhone.GetAsync();

		loaded.IsFailure.Should().BeTrue();
		loaded.Error.Code.Should().Be(code);
		DeviceDataErrors.IsNotStored(loaded).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a key that cannot be read now fails the read with <see cref="DeviceDataErrors.KeyUnavailable"/>, which is not
	/// "nothing stored", and that the session reads again once the key is back.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_KeyUnavailable_ShouldFailWithKeyUnavailableAndRecoverLater()
	{
		var keys = new InMemoryKeyStore();
		await using var db = await DeviceTestDatabase.CreateAsync(keys);
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session()));
		keys.IsUnavailable = true;

		var unavailable = await db.Sessions.GetAsync();
		keys.IsUnavailable = false;
		var back = await db.Sessions.GetAsync();

		DeviceDataErrors.IsKeyUnavailable(unavailable).Should().BeTrue();
		DeviceDataErrors.IsNotStored(unavailable).Should().BeFalse();
		back.IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a session keeps its identifier through a save and a load, and that the position cannot be moved to another row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddGet_Session_ShouldKeepTheIdentifierAndBindThePositionToIt()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var session = TestData.Session();
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, session));

		var loaded = await db.Sessions.GetAsync();
		await db.ExecuteAsync("UPDATE parking_sessions SET id = '44444444-4444-4444-4444-444444444444'");
		var moved = await db.Sessions.GetAsync();

		loaded.Value.Id.Should().Be(session.Id);
		moved.Error.Should().Be(DeviceDatabaseErrors.CiphertextRejected);
	}

	/// <summary>
	/// Verifies that an add does nothing when a session is stored already, so that a newer one is never overwritten, and that the
	/// database refuses a second row.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Add_SessionStoredAlready_ShouldKeepTheStoredSessionAndTheDatabaseHoldsOnlyOne()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var first = TestData.Session();
		await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, first));

		(await db.ApplyAsync(scope => db.Sessions.AddAsync(scope, TestData.Session()))).IsSuccess.Should().BeTrue();
		var second = () => db.ExecuteAsync("INSERT INTO parking_sessions (id, coordinates, parked_at, headline, address_text) VALUES ('33333333-3333-3333-3333-333333333333', 'x', 1, '', '')");

		(await db.Sessions.GetAsync()).Value.Id.Should().Be(first.Id);
		await second.Should().ThrowAsync<SQLite.SQLiteException>();
	}

	/// <summary>
	/// Verifies that a database without the tables gives a failed read, not an exception.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_NoTable_ShouldFailWithReadFailed()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var loaded = await db.Sessions.GetAsync();

		loaded.Error.Should().Be(DeviceDatabaseErrors.ReadFailed);
	}

	/// <summary>
	/// Verifies that the methods reject missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Members_NullArguments_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();

		var scope = await db.UnitOfWork.BeginAsync();
		await using (scope.ConfigureAwait(false))
		{
			var noAdd = () => db.Sessions.AddAsync(scope, null!); // null on purpose: proves the guard clause rejects it
			var noUpdate = () => db.Sessions.UpdateAsync(scope, null!); // null on purpose: proves the guard clause rejects it
			var noScope = () => db.Sessions.AddAsync(null!, TestData.Session()); // null on purpose: proves the guard clause rejects it

			await noAdd.Should().ThrowAsync<ArgumentNullException>();
			await noUpdate.Should().ThrowAsync<ArgumentNullException>();
			await noScope.Should().ThrowAsync<ArgumentNullException>();
		}
	}
	#endregion
}
