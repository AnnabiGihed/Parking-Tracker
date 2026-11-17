using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Infrastructure.Features.Shared.Legacy;

namespace ParkingTracker.Infrastructure.Tests.Features.Shared.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CompositeLegacyStorageReader"/> (ADR-0008): the primary answer is final when it succeeds
///              (also when empty), the fallback is asked only after a failure of the primary reader, an exception counts as a failure,
///              both failures give one failure with both reasons, and a cancellation is not swallowed.
/// </summary>
public sealed class CompositeLegacyStorageReaderTests
{
	#region Fields
	/// <summary>
	/// The primary reader double.
	/// </summary>
	private readonly ILegacyStorageReader primary = Substitute.For<ILegacyStorageReader>();

	/// <summary>
	/// The fallback reader double.
	/// </summary>
	private readonly ILegacyStorageReader fallback = Substitute.For<ILegacyStorageReader>();
	#endregion

	#region Read Tests
	/// <summary>
	/// Verifies that a successful primary reading is returned and the fallback is not asked.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_PrimarySucceeds_ShouldNotAskTheFallback()
	{
		primary.ReadAsync(Arg.Any<CancellationToken>()).Returns(Values(LegacyStorageReading.LevelDbReader, "pt.card"));

		var reading = await Create().ReadAsync(CancellationToken.None);

		reading.Reader.Should().Be("leveldb");
		reading.Values.Should().ContainKey("pt.card");
		await fallback.DidNotReceive().ReadAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that "nothing there" of the primary reader (a new install) is final and does not start a web view.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_PrimaryFindsNothing_ShouldNotAskTheFallback()
	{
		primary.ReadAsync(Arg.Any<CancellationToken>()).Returns(Values(LegacyStorageReading.LevelDbReader));

		var reading = await Create().ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeTrue();
		reading.Values.Should().BeEmpty();
		await fallback.DidNotReceive().ReadAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the fallback values are used when the primary reader fails, with the name of the fallback reader.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_PrimaryFails_ShouldUseTheFallbackValues()
	{
		primary.ReadAsync(Arg.Any<CancellationToken>()).Returns(LegacyStorageReading.Failure("unsupported compression"));
		fallback.ReadAsync(Arg.Any<CancellationToken>()).Returns(Values(LegacyStorageReading.WebViewReader, "pt.card", "pt.car"));

		var reading = await Create().ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeTrue();
		reading.Reader.Should().Be("webview");
		reading.Values.Keys.Should().BeEquivalentTo("pt.card", "pt.car");
	}

	/// <summary>
	/// Verifies that an empty answer of the fallback settles a primary failure as "nothing found", which is not retried.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_PrimaryFailsAndFallbackIsEmpty_ShouldBeNothingFound()
	{
		primary.ReadAsync(Arg.Any<CancellationToken>()).Returns(LegacyStorageReading.Failure("no pt.* key could be decoded"));
		fallback.ReadAsync(Arg.Any<CancellationToken>()).Returns(Values(LegacyStorageReading.WebViewReader));

		var reading = await Create().ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeTrue();
		reading.Values.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that two failures give one failure that names both reasons.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_BothFail_ShouldFailWithBothReasons()
	{
		primary.ReadAsync(Arg.Any<CancellationToken>()).Returns(LegacyStorageReading.Failure("corrupt table"));
		fallback.ReadAsync(Arg.Any<CancellationToken>()).Returns(LegacyStorageReading.Failure("no answer"));

		var reading = await Create().ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeFalse();
		reading.Problem.Should().Contain("corrupt table").And.Contain("no answer");
	}

	/// <summary>
	/// Verifies that exceptions of both readers are failures and not exceptions of the composite.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_BothThrow_ShouldFail()
	{
		primary.ReadAsync(Arg.Any<CancellationToken>()).Returns<Task<LegacyStorageReading>>(_ => throw new IOException("disk"));
		fallback.ReadAsync(Arg.Any<CancellationToken>()).Returns<Task<LegacyStorageReading>>(_ => throw new InvalidOperationException("no web view"));

		var reading = await Create().ReadAsync(CancellationToken.None);

		reading.Succeeded.Should().BeFalse();
		reading.Problem.Should().Contain("IOException").And.Contain("InvalidOperationException");
	}

	/// <summary>
	/// Verifies that a cancellation is not turned into a failure and does not start the fallback.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ReadAsync_Cancelled_ShouldPropagate()
	{
		primary.ReadAsync(Arg.Any<CancellationToken>()).Returns<Task<LegacyStorageReading>>(_ => throw new OperationCanceledException());

		var read = () => Create().ReadAsync(CancellationToken.None);

		await read.Should().ThrowAsync<OperationCanceledException>();
		await fallback.DidNotReceive().ReadAsync(Arg.Any<CancellationToken>());
	}
	#endregion

	#region Constructor Tests
	/// <summary>
	/// Verifies that every dependency is required.
	/// </summary>
	[Fact]
	public void Constructor_NullArgument_ShouldThrow()
	{
		var logger = NullLogger<CompositeLegacyStorageReader>.Instance;

		var calls = new Action[]
		{
			() => _ = new CompositeLegacyStorageReader(null!, fallback, logger), // null on purpose: proves the guard clause rejects it
			() => _ = new CompositeLegacyStorageReader(primary, null!, logger), // null on purpose: proves the guard clause rejects it
			() => _ = new CompositeLegacyStorageReader(primary, fallback, null!), // null on purpose: proves the guard clause rejects it
		};

		foreach (var call in calls)
		{
			call.Should().Throw<ArgumentNullException>();
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Creates the composite over the two doubles.
	/// </summary>
	/// <returns>The composite.</returns>
	private CompositeLegacyStorageReader Create()
	{
		return new CompositeLegacyStorageReader(primary, fallback, NullLogger<CompositeLegacyStorageReader>.Instance);
	}

	/// <summary>
	/// Builds a successful reading with the given keys.
	/// </summary>
	/// <param name="reader">The name of the reader.</param>
	/// <param name="keys">The keys; each gets the value <c>{}</c>.</param>
	/// <returns>The reading.</returns>
	private static LegacyStorageReading Values(string reader, params string[] keys)
	{
		return LegacyStorageReading.Success(keys.ToDictionary(key => key, _ => "{}", StringComparer.Ordinal), reader);
	}
	#endregion
}
