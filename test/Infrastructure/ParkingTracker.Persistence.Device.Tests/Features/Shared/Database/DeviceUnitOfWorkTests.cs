using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Testing.Device;

namespace ParkingTracker.Persistence.Device.Tests.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="DeviceUnitOfWork"/> and its scopes against a real SQLite file: staged changes are applied
///              together on commit, a refused change rolls all of them back and gives a failed result, every scope owns its staged
///              changes, one scope at a time holds the write gate, a disposed scope drops what it staged, and a cancelled token stops
///              the commit before the transaction starts.
/// </summary>
public sealed class DeviceUnitOfWorkTests
{
	#region Commit Tests
	/// <summary>
	/// Verifies that nothing is written before the commit and everything after it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Commit_StagedChanges_ShouldApplyAllOfThemOnlyAtCommit()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var scope = DeviceChangeScope.From(await db.UnitOfWork.BeginAsync());
		await using (scope.ConfigureAwait(false))
		{
			scope.Stage(connection => connection.Execute("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (1, 'a', 1)"));
			scope.Stage(connection => connection.Execute("UPDATE device_owner SET account_hash = 'b' WHERE id = 1"));
			(await db.ScalarAsync<int>("SELECT COUNT(*) FROM device_owner")).Should().Be(0);

			var result = await scope.CommitAsync();

			result.IsSuccess.Should().BeTrue();
		}

		(await db.ScalarAsync<string>("SELECT account_hash FROM device_owner")).Should().Be("b");
	}

	/// <summary>
	/// Verifies that a commit with nothing staged succeeds.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Commit_NothingStaged_ShouldSucceed()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var scope = await db.UnitOfWork.BeginAsync();
		await using (scope.ConfigureAwait(false))
		{
			var result = await scope.CommitAsync();

			result.IsSuccess.Should().BeTrue();
		}
	}
	#endregion

	#region Scope Tests
	/// <summary>
	/// Verifies that a scope that is disposed without a commit drops what it staged, so that the next scope never applies it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Dispose_WithoutCommit_ShouldDropTheStagedChanges()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var abandoned = DeviceChangeScope.From(await db.UnitOfWork.BeginAsync());
		abandoned.Stage(connection => connection.Execute("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (1, 'a', 1)"));
		await abandoned.DisposeAsync();

		var next = await db.UnitOfWork.BeginAsync();
		await using (next.ConfigureAwait(false))
		{
			(await next.CommitAsync()).IsSuccess.Should().BeTrue();
		}

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM device_owner")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a second scope waits until the first one is disposed, and that the changes of the two are never mixed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Begin_WhileAnotherScopeIsOpen_ShouldWaitForItAndKeepTheChangesApart()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var first = DeviceChangeScope.From(await db.UnitOfWork.BeginAsync());
		first.Stage(connection => connection.Execute("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (1, 'first', 1)"));
		var secondBegin = db.UnitOfWork.BeginAsync();

		var waitedForFirst = await Task.WhenAny(secondBegin, Task.Delay(TimeSpan.FromMilliseconds(250))) != secondBegin;
		await first.DisposeAsync();
		var second = DeviceChangeScope.From(await secondBegin);
		await using (second.ConfigureAwait(false))
		{
			second.Stage(connection => connection.Execute("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (1, 'second', 1)"));
			(await second.CommitAsync()).IsSuccess.Should().BeTrue();
		}

		waitedForFirst.Should().BeTrue();
		(await db.ScalarAsync<string>("SELECT account_hash FROM device_owner")).Should().Be("second");
	}

	/// <summary>
	/// Verifies that a scope that was disposed refuses to stage or to commit.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task DisposedScope_StageOrCommit_ShouldThrow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var scope = DeviceChangeScope.From(await db.UnitOfWork.BeginAsync());
		await scope.DisposeAsync();
		await scope.DisposeAsync();

		var stage = () => scope.Stage(connection => connection.Execute("SELECT 1"));
		var commit = () => scope.CommitAsync();

		stage.Should().Throw<ObjectDisposedException>();
		await commit.Should().ThrowAsync<ObjectDisposedException>();
	}

	/// <summary>
	/// Verifies that a wait for the gate can be cancelled.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Begin_CancelledWhileWaiting_ShouldThrow()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var first = await db.UnitOfWork.BeginAsync();
		await using (first.ConfigureAwait(false))
		{
			using var cancelled = new CancellationTokenSource();
			var second = db.UnitOfWork.BeginAsync(cancelled.Token);
			await cancelled.CancelAsync();

			var wait = async () => _ = await second.ConfigureAwait(false);

			await wait.Should().ThrowAsync<OperationCanceledException>();
		}
	}

	/// <summary>
	/// Verifies that a scope that does not come from the unit of work is refused.
	/// </summary>
	[Fact]
	public void From_ForeignScope_ShouldThrow()
	{
		var foreign = Substitute.For<IDeviceChangeScope>();

		var noScope = () => DeviceChangeScope.From(null!); // null on purpose: proves the guard clause rejects it
		var other = () => DeviceChangeScope.From(foreign);

		noScope.Should().Throw<ArgumentNullException>();
		other.Should().Throw<ArgumentException>();
	}
	#endregion

	#region Rollback Tests
	/// <summary>
	/// Verifies that one refused change rolls back the changes before it, gives a failed result and drops the staged changes, so that
	/// the next commit of the scope does not repeat them.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Commit_OneChangeRefused_ShouldRollBackEverythingAndClearTheStagedChanges()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var scope = DeviceChangeScope.From(await db.UnitOfWork.BeginAsync());
		await using (scope.ConfigureAwait(false))
		{
			scope.Stage(connection => connection.Execute("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (1, 'a', 1)"));
			scope.Stage(connection => connection.Execute("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (2, 'b', 1)"));

			var failed = await scope.CommitAsync();
			var next = await scope.CommitAsync();

			failed.IsFailure.Should().BeTrue();
			failed.Error.Should().Be(DeviceDatabaseErrors.CommitFailed);
			next.IsSuccess.Should().BeTrue();
		}

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM device_owner")).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a cancelled token stops the commit, and that the staged changes are dropped rather than kept.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Commit_Cancelled_ShouldThrowAndDropTheStagedChanges()
	{
		await using var db = await DeviceTestDatabase.CreateAsync();
		var scope = DeviceChangeScope.From(await db.UnitOfWork.BeginAsync());
		await using (scope.ConfigureAwait(false))
		{
			scope.Stage(connection => connection.Execute("INSERT INTO device_owner (id, account_hash, bound_at) VALUES (1, 'a', 1)"));
			using var cancelled = new CancellationTokenSource();
			await cancelled.CancelAsync();

			var commit = () => scope.CommitAsync(cancelled.Token);

			await commit.Should().ThrowAsync<OperationCanceledException>();
			(await scope.CommitAsync()).IsSuccess.Should().BeTrue();
		}

		(await db.ScalarAsync<int>("SELECT COUNT(*) FROM device_owner")).Should().Be(0);
	}
	#endregion

	#region Argument Tests
	/// <summary>
	/// Verifies that the constructor and the staging method reject missing arguments.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Members_NullArguments_ShouldThrow()
	{
		await using var db = DeviceTestDatabase.CreateEmpty();
		var scope = DeviceChangeScope.From(await db.UnitOfWork.BeginAsync());
		await using (scope.ConfigureAwait(false))
		{
			var noDatabase = () => new DeviceUnitOfWork(null!, NullLogger<DeviceUnitOfWork>.Instance); // null on purpose: proves the guard clause rejects it
			var noLogger = () => new DeviceUnitOfWork(db.Database, null!); // null on purpose: proves the guard clause rejects it
			var noChange = () => scope.Stage(null!); // null on purpose: proves the guard clause rejects it

			noDatabase.Should().Throw<ArgumentNullException>();
			noLogger.Should().Throw<ArgumentNullException>();
			noChange.Should().Throw<ArgumentNullException>();
		}
	}
	#endregion
}
