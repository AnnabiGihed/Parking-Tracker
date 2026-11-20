using Microsoft.Extensions.Time.Testing;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Infrastructure.Features.AddressSearch;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Tests.Features.AddressSearch;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests for <see cref="AddressSearchGate"/> on a fake clock: a question answered before is answered from memory even
///              while another request is under way, a cancelled wait frees the turn and does not count as a request, the memory is
///              emptied when it is full, and requests go through one at a time.
/// </summary>
public sealed class AddressSearchGateTests
{
	#region Static Instances
	/// <summary>
	/// The matches the sends answer with.
	/// </summary>
	private static readonly IReadOnlyList<AddressMatch> Answer = [new AddressMatch(new GeoPoint(50.85, 4.35), "Rue Neuve")];
	#endregion

	#region Memory Tests
	/// <summary>
	/// Verifies that a question answered before is answered at once while another request is still under way, without waiting for
	/// its turn.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Run_AnsweredQuestionWhileBusy_ShouldAnswerFromMemoryAtOnce()
	{
		var clock = new FakeTimeProvider();
		using var gate = new AddressSearchGate(clock);
		await gate.RunAsync("known", 8, Succeed, CancellationToken.None);
		var release = new TaskCompletionSource<Result<IReadOnlyList<AddressMatch>>>();
		clock.Advance(TimeSpan.FromSeconds(1));
		var busy = gate.RunAsync("other", 8, () => release.Task, CancellationToken.None);
		var sends = 0;

		var again = await gate.RunAsync("known", 8, () => CountedSucceed(ref sends), CancellationToken.None);

		again.Value.Should().Equal(Answer);
		sends.Should().Be(0);
		busy.IsCompleted.Should().BeFalse();
		release.SetResult(Result.Success(Answer));
		await busy;
	}

	/// <summary>
	/// Verifies that the memory is emptied when it is full: the oldest question is sent again after one more question than the
	/// memory holds.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Run_MoreQuestionsThanTheMemoryHolds_ShouldForgetTheEarlierOnes()
	{
		var clock = new FakeTimeProvider();
		using var gate = new AddressSearchGate(clock);
		var sends = 0;
		for (var index = 0; index <= AddressSearchGate.MaxCached; index++)
		{
			clock.Advance(TimeSpan.FromSeconds(1));
			await gate.RunAsync($"question {index}", 8, () => CountedSucceed(ref sends), CancellationToken.None);
		}

		sends.Should().Be(AddressSearchGate.MaxCached + 1);
		clock.Advance(TimeSpan.FromSeconds(1));
		await gate.RunAsync("question 0", 8, () => CountedSucceed(ref sends), CancellationToken.None);

		sends.Should().Be(AddressSearchGate.MaxCached + 2);
	}
	#endregion

	#region Pace Tests
	/// <summary>
	/// Verifies that cancelling a call that waits for the interval frees the turn and does not count as a request: the next call
	/// is sent a second after the last real request, not a second after the cancelled one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Run_CancelledWhileWaiting_ShouldFreeTheTurnAndKeepTheLastRequest()
	{
		var clock = new FakeTimeProvider();
		using var gate = new AddressSearchGate(clock);
		await gate.RunAsync("first", 8, Succeed, CancellationToken.None);
		clock.Advance(TimeSpan.FromMilliseconds(400));
		using var cancellation = new CancellationTokenSource();
		var cancelled = gate.RunAsync("second", 8, Succeed, cancellation.Token);
		cancelled.IsCompleted.Should().BeFalse();

		await cancellation.CancelAsync();
		await FluentActions.Awaiting(() => cancelled).Should().ThrowAsync<OperationCanceledException>();
		clock.Advance(TimeSpan.FromMilliseconds(600));
		var third = gate.RunAsync("third", 8, Succeed, CancellationToken.None);

		third.IsCompleted.Should().BeTrue();
		(await third).IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that two calls at the same time go through one at a time: the second request is not sent before the first ended.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Run_TwoConcurrentCalls_ShouldBeSentOneAtATime()
	{
		var clock = new FakeTimeProvider();
		using var gate = new AddressSearchGate(clock);
		var events = new List<string>();
		var release = new TaskCompletionSource<Result<IReadOnlyList<AddressMatch>>>();
		var first = gate.RunAsync("one", 8, async () =>
		{
			events.Add("start one");
			var result = await release.Task.ConfigureAwait(false);
			events.Add("end one");
			return result;
		}, CancellationToken.None);
		var second = gate.RunAsync("two", 8, () =>
		{
			events.Add("start two");
			return Succeed();
		}, CancellationToken.None);

		clock.Advance(TimeSpan.FromSeconds(2));
		events.Should().Equal("start one");
		release.SetResult(Result.Success(Answer));
		await first;
		clock.Advance(TimeSpan.FromSeconds(1));
		await second;

		events.Should().Equal("start one", "end one", "start two");
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Answers a request with a success.
	/// </summary>
	/// <returns>A completed task with the matches.</returns>
	private static Task<Result<IReadOnlyList<AddressMatch>>> Succeed() => Task.FromResult(Result.Success(Answer));

	/// <summary>
	/// Answers a request with a success and counts it.
	/// </summary>
	/// <param name="sends">The counter of the requests sent.</param>
	/// <returns>A completed task with the matches.</returns>
	private static Task<Result<IReadOnlyList<AddressMatch>>> CountedSucceed(ref int sends)
	{
		sends++;
		return Succeed();
	}
	#endregion
}
