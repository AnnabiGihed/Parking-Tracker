using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="LegacyDataMigration"/> (ADR-0004, ADR-0008): what is copied where, that unreadable values are
///              skipped, that values already set are kept, that the marker (with the reader that was used) is written only after a
///              successful read, and the retry and re-entry fallback after failed reads, time-outs of the whole run, failed saves and
///              unexpected exceptions, each of which counts as an attempt.
/// </summary>
public sealed class LegacyDataMigrationTests
{
	#region Constants
	/// <summary>
	/// A card as the old app wrote it.
	/// </summary>
	private const string CardJson = """{"number":"TEST","plate":"1TST000","sector":"louise","from":"2026-01-01","to":"2099-12-31","tolerances":[]}""";

	/// <summary>
	/// A parked car as the old app wrote it.
	/// </summary>
	private const string CarJson = """{"lat":50.8265,"lon":4.365,"t":1760000000000,"status":"Parked","where":"Avenue Louise"}""";

	/// <summary>
	/// A parking timer as the old app wrote it.
	/// </summary>
	private const string TimerJson = """{"end":1760003600000,"minutes":60}""";
	#endregion

	#region Fields
	/// <summary>
	/// The encrypted storage double.
	/// </summary>
	private readonly InMemoryKeyValueStorage secure = new();

	/// <summary>
	/// The plain storage double.
	/// </summary>
	private readonly InMemoryKeyValueStorage plain = new();

	/// <summary>
	/// The reader of the old storage.
	/// </summary>
	private readonly ILegacyStorageReader reader = Substitute.For<ILegacyStorageReader>();

	/// <summary>
	/// The clock.
	/// </summary>
	private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero));
	#endregion

	#region Copy Tests
	/// <summary>
	/// Verifies that the card goes to the encrypted storage only, the other values to the plain one, and the marker lists what came.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_AllKeys_ShouldCopyCardToSecureAndTheRestToPlain()
	{
		Returns(Success(("pt.card", CardJson), ("pt.car", CarJson), ("pt.timer", TimerJson), ("pt.bgAlerts", "false"), ("pt.layers", """{"works":false}"""), ("pt.permSnooze", "1760000000000")));

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.Migrated);
		secure.Values.Keys.Should().Equal("pt.card");
		plain.Values.Should().ContainKeys("pt.car", "pt.timer", "pt.bgAlerts", "pt.layers", "pt.permSnooze", "pt.migration");
		plain.Values["pt.car"].Should().Be(CarJson);
		plain.Values["pt.bgAlerts"].Should().Be("false");
		using var marker = JsonDocument.Parse(plain.Values["pt.migration"]);
		marker.RootElement.GetProperty("version").GetInt32().Should().Be(1);
		marker.RootElement.GetProperty("outcome").GetString().Should().Be("migrated");
		marker.RootElement.GetProperty("reader").GetString().Should().Be("leveldb");
		marker.RootElement.GetProperty("found").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo("pt.card", "pt.car", "pt.timer", "pt.bgAlerts", "pt.layers", "pt.permSnooze");
		marker.RootElement.GetProperty("at").GetInt64().Should().Be(clock.GetUtcNow().ToUnixTimeMilliseconds());
	}

	/// <summary>
	/// Verifies that the copied card reads back through the settings store, as the app will read it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_Card_ShouldReadBackThroughTheSettingsStore()
	{
		Returns(Success(("pt.card", CardJson)));
		var migration = Create();

		await migration.RunOnceAsync();

		var card = await new SettingsStore(secure, plain).GetCardAsync();
		card!.Sector.Should().Be("louise");
		card.Plate.Should().Be("1TST000");
	}

	/// <summary>
	/// Verifies that keys the app does not migrate, such as the new welcome flag, are left out.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_UnknownKeys_ShouldBeIgnored()
	{
		Returns(Success(("pt.welcomeSeen", "true"), ("pt.other", "1")));

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.NothingFound);
		plain.Values.Keys.Should().Equal("pt.migration");
	}

	/// <summary>
	/// Verifies that a value the new app already holds is not replaced, for the card and for a plain key.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_ValuesAlreadySet_ShouldKeepThem()
	{
		secure.Values["pt.card"] = """{"sector":"new"}""";
		plain.Values["pt.car"] = "mine";
		Returns(Success(("pt.card", CardJson), ("pt.car", CarJson), ("pt.timer", TimerJson)));

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.Migrated);
		secure.Values["pt.card"].Should().Be("""{"sector":"new"}""");
		plain.Values["pt.car"].Should().Be("mine");
		plain.Values["pt.timer"].Should().Be(TimerJson);
	}

	/// <summary>
	/// Verifies that unreadable values are skipped without failing the migration, and the marker is still written.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_UnreadableValues_ShouldSkipThemAndStillMark()
	{
		Returns(Success(("pt.card", "garbage"), ("pt.car", "{}"), ("pt.timer", "[]"), ("pt.bgAlerts", "maybe"), ("pt.layers", "[1]"), ("pt.permSnooze", "soon")));

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.NothingFound);
		secure.Values.Should().BeEmpty();
		plain.Values.Keys.Should().Equal("pt.migration");
	}

	/// <summary>
	/// Verifies that one unreadable value does not stop the readable ones.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_OneBadValue_ShouldCopyTheOthers()
	{
		Returns(Success(("pt.card", "garbage"), ("pt.car", CarJson)));

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.Migrated);
		plain.Values.Should().ContainKey("pt.car");
		secure.Values.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a new install, whose old storage is empty, gets the marker and no values.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_NothingInOldStorage_ShouldWriteTheMarkerOnly()
	{
		Returns(Success());

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.NothingFound);
		using var marker = JsonDocument.Parse(plain.Values["pt.migration"]);
		marker.RootElement.GetProperty("outcome").GetString().Should().Be("nothing");
	}

	/// <summary>
	/// Verifies that the marker names the reader that found the values, here the fallback web view.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_ValuesFromTheWebView_ShouldRecordThatReader()
	{
		Returns(LegacyStorageReading.Success(new Dictionary<string, string>(StringComparer.Ordinal) { ["pt.car"] = CarJson }, LegacyStorageReading.WebViewReader));

		await Create().RunOnceAsync();

		using var marker = JsonDocument.Parse(plain.Values["pt.migration"]);
		marker.RootElement.GetProperty("reader").GetString().Should().Be("webview");
	}
	#endregion

	#region Once Tests
	/// <summary>
	/// Verifies that an existing marker stops the migration before the old storage is read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_MarkerExists_ShouldNotReadTheOldStorage()
	{
		plain.Values["pt.migration"] = "{}";

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.AlreadyDone);
		await reader.DidNotReceiveWithAnyArgs().ReadAsync(default);
	}

	/// <summary>
	/// Verifies that every caller in a process gets the same run, so that the old storage is read once.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_CalledTwice_ShouldShareOneRun()
	{
		Returns(Success(("pt.car", CarJson)));
		var migration = Create();

		var first = migration.RunOnceAsync();
		var second = migration.RunOnceAsync();

		second.Should().BeSameAs(first);
		await first;
		await reader.Received(1).ReadAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the marker can be read back after the run, and is absent before it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadMarkerAsync_BeforeAndAfterTheRun_ShouldGiveNullThenTheMarker()
	{
		Returns(Success(("pt.car", CarJson)));
		var migration = Create();
		(await migration.ReadMarkerAsync()).Should().BeNull();

		await migration.RunOnceAsync();

		(await migration.ReadMarkerAsync()).Should().Contain("\"outcome\":\"migrated\"");
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that a failed read writes no marker, so that the next launch tries again, and counts the attempt.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_FailedRead_ShouldWriteNoMarkerAndCountTheAttempt()
	{
		Returns(LegacyStorageReading.Failure("disk"));

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.WillRetry);
		plain.Values.Should().NotContainKey("pt.migration");
		plain.Values["pt.migrationAttempts"].Should().Be("1");
	}

	/// <summary>
	/// Verifies that the third failed launch gives up: the marker records it and the user enters the card again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_ThirdFailedLaunch_ShouldGiveUpWithAMarker()
	{
		Returns(LegacyStorageReading.Failure("disk"));

		var outcomes = new List<LegacyMigrationOutcome>();
		for (var launch = 0; launch < LegacyDataMigration.MaxAttempts; launch++)
		{
			outcomes.Add(await Create().RunOnceAsync());
		}

		outcomes.Should().Equal(LegacyMigrationOutcome.WillRetry, LegacyMigrationOutcome.WillRetry, LegacyMigrationOutcome.GaveUp);
		using var marker = JsonDocument.Parse(plain.Values["pt.migration"]);
		marker.RootElement.GetProperty("outcome").GetString().Should().Be("gaveUp");
		marker.RootElement.GetProperty("reader").GetString().Should().Be("none");
		(await Create().RunOnceAsync()).Should().Be(LegacyMigrationOutcome.AlreadyDone);
	}

	/// <summary>
	/// Verifies that a read that works after a failed one migrates normally.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_FailedThenSuccessfulLaunch_ShouldMigrate()
	{
		Returns(LegacyStorageReading.Failure("disk"));
		await Create().RunOnceAsync();
		Returns(Success(("pt.car", CarJson)));

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.Migrated);
		plain.Values.Should().ContainKey("pt.migration");
	}

	/// <summary>
	/// Verifies that an exception of the reader is a failed read, not a crash.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_ReaderThrows_ShouldBeAFailedRead()
	{
		reader.ReadAsync(Arg.Any<CancellationToken>()).Returns<Task<LegacyStorageReading>>(_ => throw new InvalidOperationException("boom"));

		var outcome = await Create().RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.WillRetry);
		plain.Values.Should().NotContainKey("pt.migration");
	}

	/// <summary>
	/// Verifies that a read that takes longer than the time limit of the run is a failed attempt and is counted.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_ReaderTooSlow_ShouldCountAFailedAttempt()
	{
		var started = new TaskCompletionSource();
		reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(async call =>
		{
			started.SetResult();
			await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()).ConfigureAwait(false);
			return LegacyStorageReading.Success(new Dictionary<string, string>(StringComparer.Ordinal), LegacyStorageReading.LevelDbReader);
		});

		var run = Create().RunOnceAsync();
		await started.Task;
		clock.Advance(LegacyDataMigration.RunTimeout + TimeSpan.FromSeconds(1));

		(await run).Should().Be(LegacyMigrationOutcome.WillRetry);
		plain.Values.Should().NotContainKey("pt.migration");
		plain.Values["pt.migrationAttempts"].Should().Be("1");
	}

	/// <summary>
	/// Verifies that the whole run is limited even when the reader ignores its cancellation token: the start-up gate must open.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_ReaderIgnoresCancellation_ShouldStillEndAtTheRunTimeout()
	{
		var started = new TaskCompletionSource();
		var never = new TaskCompletionSource<LegacyStorageReading>();
		reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ =>
		{
			started.SetResult();
			return never.Task;
		});

		var run = Create().RunOnceAsync();
		await started.Task;
		clock.Advance(LegacyDataMigration.RunTimeout + TimeSpan.FromSeconds(1));

		(await run).Should().Be(LegacyMigrationOutcome.WillRetry);
		plain.Values["pt.migrationAttempts"].Should().Be("1");
	}

	/// <summary>
	/// Verifies that the third time-out gives up, so that "three tries at most" also holds for a run that is too slow.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_ThirdTimeout_ShouldGiveUp()
	{
		plain.Values["pt.migrationAttempts"] = "2";
		var started = new TaskCompletionSource();
		reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ =>
		{
			started.SetResult();
			return new TaskCompletionSource<LegacyStorageReading>().Task;
		});

		var run = Create().RunOnceAsync();
		await started.Task;
		clock.Advance(LegacyDataMigration.RunTimeout + TimeSpan.FromSeconds(1));

		(await run).Should().Be(LegacyMigrationOutcome.GaveUp);
		plain.Values.Should().ContainKey("pt.migration");
	}

	/// <summary>
	/// Verifies that an unexpected exception of the storage (the catch-all path) counts as an attempt too.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_MarkerReadThrows_ShouldCountTheAttempt()
	{
		var storage = Substitute.For<IKeyValueStorage>();
		storage.ReadAsync(LegacyDataMigration.MarkerKey).Returns<Task<string?>>(_ => throw new IOException("preferences"));
		storage.ReadAsync(LegacyDataMigration.AttemptsKey).Returns("1");

		var outcome = await new LegacyDataMigration(reader, new SettingsStore(secure, storage), storage, clock, NullLogger<LegacyDataMigration>.Instance).RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.WillRetry);
		await storage.Received().WriteAsync(LegacyDataMigration.AttemptsKey, "2");
	}

	/// <summary>
	/// Verifies that the catch-all path gives up after the allowed number of attempts.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_MarkerReadThrowsInTheLastAttempt_ShouldGiveUp()
	{
		var storage = Substitute.For<IKeyValueStorage>();
		storage.ReadAsync(LegacyDataMigration.MarkerKey).Returns<Task<string?>>(_ => throw new IOException("preferences"));
		storage.ReadAsync(LegacyDataMigration.AttemptsKey).Returns("2");

		var outcome = await new LegacyDataMigration(reader, new SettingsStore(secure, storage), storage, clock, NullLogger<LegacyDataMigration>.Instance).RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.GaveUp);
		await storage.Received().WriteAsync(LegacyDataMigration.AttemptsKey, "3");
	}

	/// <summary>
	/// Verifies that a value that cannot be saved counts as an attempt.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_SaveFails_ShouldCountTheAttempt()
	{
		var failing = Substitute.For<IKeyValueStorage>();
		failing.ReadAsync(Arg.Any<string>()).Returns((string?)null);
		failing.WriteAsync("pt.car", Arg.Any<string>()).Returns<Task>(_ => throw new IOException("full"));
		Returns(Success(("pt.car", CarJson)));

		await new LegacyDataMigration(reader, new SettingsStore(secure, failing), failing, clock, NullLogger<LegacyDataMigration>.Instance).RunOnceAsync();

		await failing.Received().WriteAsync(LegacyDataMigration.AttemptsKey, "1");
	}

	/// <summary>
	/// Verifies that a value that cannot be saved leaves no marker, so that the next launch copies it, while the values saved
	/// before it stay.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_SaveFails_ShouldWriteNoMarker()
	{
		var failing = Substitute.For<IKeyValueStorage>();
		failing.ReadAsync(Arg.Any<string>()).Returns((string?)null);
		failing.WriteAsync("pt.car", Arg.Any<string>()).Returns<Task>(_ => throw new IOException("full"));
		Returns(Success(("pt.car", CarJson), ("pt.timer", TimerJson)));

		var outcome = await new LegacyDataMigration(reader, new SettingsStore(secure, failing), failing, clock, NullLogger<LegacyDataMigration>.Instance).RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.WillRetry);
		await failing.DidNotReceive().WriteAsync("pt.migration", Arg.Any<string>());
		await failing.Received().WriteAsync("pt.timer", TimerJson);
	}

	/// <summary>
	/// Verifies that a failure of the storage that holds the marker does not escape.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RunOnceAsync_MarkerStorageThrows_ShouldNotThrow()
	{
		var broken = Substitute.For<IKeyValueStorage>();
		broken.ReadAsync(Arg.Any<string>()).Returns<Task<string?>>(_ => throw new InvalidOperationException("no preferences"));

		var outcome = await new LegacyDataMigration(reader, new SettingsStore(secure, broken), broken, clock, NullLogger<LegacyDataMigration>.Instance).RunOnceAsync();

		outcome.Should().Be(LegacyMigrationOutcome.WillRetry);
	}
	#endregion

	#region Constructor Tests
	/// <summary>
	/// Verifies that every dependency is required.
	/// </summary>
	[Fact]
	public void Constructor_NullArgument_ShouldThrow()
	{
		var settings = new SettingsStore(secure, plain);
		var logger = NullLogger<LegacyDataMigration>.Instance;

		var calls = new Action[]
		{
			() => _ = new LegacyDataMigration(null!, settings, plain, clock, logger), // null on purpose: proves the guard clause rejects it
			() => _ = new LegacyDataMigration(reader, null!, plain, clock, logger), // null on purpose: proves the guard clause rejects it
			() => _ = new LegacyDataMigration(reader, settings, null!, clock, logger), // null on purpose: proves the guard clause rejects it
			() => _ = new LegacyDataMigration(reader, settings, plain, null!, logger), // null on purpose: proves the guard clause rejects it
			() => _ = new LegacyDataMigration(reader, settings, plain, clock, null!), // null on purpose: proves the guard clause rejects it
		};

		foreach (var call in calls)
		{
			call.Should().Throw<ArgumentNullException>();
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Creates the migration over the storage doubles, as a new launch would.
	/// </summary>
	/// <returns>The migration.</returns>
	private LegacyDataMigration Create()
	{
		return new LegacyDataMigration(reader, new SettingsStore(secure, plain), plain, clock, NullLogger<LegacyDataMigration>.Instance);
	}

	/// <summary>
	/// Makes the reader give an answer.
	/// </summary>
	/// <param name="reading">The answer.</param>
	private void Returns(LegacyStorageReading reading)
	{
		reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(reading);
	}

	/// <summary>
	/// Builds a successful reading.
	/// </summary>
	/// <param name="values">The key and text pairs.</param>
	/// <returns>The reading.</returns>
	private static LegacyStorageReading Success(params (string Key, string Json)[] values)
	{
		return LegacyStorageReading.Success(values.ToDictionary(v => v.Key, v => v.Json, StringComparer.Ordinal), LegacyStorageReading.LevelDbReader);
	}
	#endregion
}
