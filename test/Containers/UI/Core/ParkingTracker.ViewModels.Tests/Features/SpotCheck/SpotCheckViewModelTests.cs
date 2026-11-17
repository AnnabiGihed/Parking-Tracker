using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SpotCheckViewModel"/>: stale fixes are ignored, a fix triggers a check by the tracking
///              rules, the lost signal, a failed location and a lost connection say "can't verify" and cannot be overwritten by a
///              check that finishes late, a tap on the map drops a pin, a failing background check is told to the user, and the
///              nearest-spot search follows an answer that is not free.
/// </summary>
public sealed class SpotCheckViewModelTests
{
	#region Fix Tests
	/// <summary>
	/// Verifies that a fix older than 15 seconds is ignored: no position, no check.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FixReceived_StaleFix_ShouldBeIgnored()
	{
		using var ui = new UiFixture();
		await ui.Spot.StartTrackingAsync();

		ui.RaiseFix(new GpsFix(UiFixture.Here, 10, null, UiFixture.Start.AddSeconds(-16)));

		ui.Spot.Me.Should().BeNull();
		await ui.Sender.DidNotReceive().Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a fresh fix while following is checked with its accuracy and the answer is shown with its chips.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FixReceived_WhileFollowing_ShouldCheckAndShowTheAnswer()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "Paid here."));
		await ui.Spot.StartTrackingAsync();

		ui.RaiseFix(ui.FixNow(accuracy: 12));

		await ui.Sender.Received(1).Send(Arg.Is<DecideSpotQuery>(q => Math.Abs((q.AccuracyMeters ?? 0) - 12) < 0.001 && Math.Abs(q.Latitude - UiFixture.Here.Latitude) < 0.000001), Arg.Any<CancellationToken>());
		ui.Spot.Status.Title.Should().Be("Paid parking");
		ui.Spot.Status.Detail.Should().Be("Paid here.");
		ui.Spot.Chips.Select(chip => chip.Text).Should().Contain("My position ±12 m");
		ui.Spot.IsChecking.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a fix is not checked when the user is not followed, but the position is kept.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FixReceived_NotFollowing_ShouldKeepThePositionWithoutChecking()
	{
		using var ui = new UiFixture();

		ui.RaiseFix(ui.FixNow());

		ui.Spot.Me.Should().NotBeNull();
		await ui.Sender.DidNotReceive().Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a small move shortly after a check does not start another one, and a move of more than 8 metres does.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FixReceived_SmallThenBigMove_ShouldCheckOnlyAfterTheBigMove()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());

		ui.RaiseFix(ui.FixNow(new GeoPoint(UiFixture.Here.Latitude + 0.00003, UiFixture.Here.Longitude)));
		await ui.Sender.Received(1).Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
		ui.RaiseFix(ui.FixNow(new GeoPoint(UiFixture.Here.Latitude + 0.0002, UiFixture.Here.Longitude)));

		await ui.Sender.Received(2).Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a better accuracy after an answer for a poor fix is checked at once.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FixReceived_AccuracyImproves_ShouldCheckAgain()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.CantVerify("GPS is not precise enough yet."));
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow(accuracy: 60));

		ui.RaiseFix(ui.FixNow(accuracy: 15));

		await ui.Sender.Received(2).Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
	}
	#endregion

	#region Tick Tests
	/// <summary>
	/// Verifies that 30 seconds without a check make the periodic look check the spot again while the fix is fresh.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Tick_ThirtySecondsWithoutCheck_ShouldCheckAgain()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());
		ui.Clock.Advance(TimeSpan.FromSeconds(25));
		ui.RaiseFix(ui.FixNow());
		await ui.Sender.Received(1).Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());

		ui.Clock.Advance(TimeSpan.FromSeconds(6));
		await ui.Spot.TickAsync();

		await ui.Sender.Received(2).Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that when the last fix is older than 15 seconds the answer says the signal is lost instead of checking.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Tick_LastFixOlderThanFifteenSeconds_ShouldSayTheSignalIsLost()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());
		ui.Clock.Advance(TimeSpan.FromSeconds(31));

		await ui.Spot.TickAsync();

		ui.Spot.Status.Title.Should().Be("Can't verify right now");
		ui.Spot.Status.Detail.Should().Be("Lost the GPS signal. Waiting for a fresh position…");
		await ui.Sender.Received(1).Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the periodic timer of the view model calls the tick every five seconds.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StartTracking_Timer_ShouldLookEveryFiveSeconds()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());
		ui.Clock.Advance(TimeSpan.FromSeconds(16));
		ui.RaiseFix(ui.FixNow());

		ui.Clock.Advance(FixWatchdog.Interval);
		ui.Clock.Advance(TimeSpan.FromSeconds(10));
		ui.Clock.Advance(TimeSpan.FromSeconds(5));

		await ui.Sender.Received().Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
		ui.Spot.IsTracking.Should().BeTrue();
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that a failed location without a pin says "can't verify", stops the following and asks for the permission banner to refresh.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task LocationError_NoPin_ShouldSayCantVerifyAndStopFollowing()
	{
		using var ui = new UiFixture();
		var failed = false;
		ui.Spot.LocationFailed += (_, _) => failed = true;
		await ui.Spot.StartTrackingAsync();

		ui.RaiseLocationError("location not allowed");

		ui.Spot.Status.Detail.Should().Be("No GPS position: location not allowed. Allow location for this app, or tap the map.");
		ui.Spot.IsTracking.Should().BeFalse();
		failed.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a failed location keeps the answer for the pin and tells the user in the toast.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task LocationError_WithPin_ShouldKeepTheAnswerAndToast()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "Paid."));
		await ui.Spot.HandleMapClickAsync(UiFixture.Here);

		ui.RaiseLocationError("no signal");

		ui.Spot.Status.Title.Should().Be("Paid parking");
		ui.Toast.Message.Should().Be("No GPS: showing the pin you dropped");
	}

	/// <summary>
	/// Verifies that a check that cannot run shows "can't verify" with the reason.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_QueryFails_ShouldShowUnexpectedError()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SpotDecision>(new Error("Validation.Failed", "latitude out of range")));

		await ui.Spot.HandleMapClickAsync(UiFixture.Here);

		ui.Spot.Status.Title.Should().Be("Can't verify right now");
		ui.Spot.Status.Detail.Should().Be("Unexpected error: latitude out of range");
	}

	/// <summary>
	/// Verifies that losing the connection says "can't verify" and that coming back checks again with a toast.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task HandleConnectivityChange_OfflineThenOnline_ShouldSayCantVerifyThenCheckAgain()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.HandleMapClickAsync(UiFixture.Here);

		await ui.Spot.HandleConnectivityChangeAsync(false);
		ui.Spot.Status.Detail.Should().Be("Internet connection lost. The app only answers with live data.");
		await ui.Spot.HandleConnectivityChangeAsync(true);

		ui.Toast.Message.Should().Be("Back online");
		await ui.Sender.Received(2).Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>());
	}
	#endregion

	#region Stale Answer Tests
	/// <summary>
	/// Verifies that a check that finishes after the lost-signal answer does not bring back an old "free" answer.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Tick_CheckFinishesAfterLostSignal_ShouldNotOverwriteCantVerify()
	{
		using var ui = new UiFixture();
		var pending = HoldChecks(ui);
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());
		ui.Clock.Advance(TimeSpan.FromSeconds(31));

		await ui.Spot.TickAsync();
		pending.SetResult(Result.Success(SpotDecision.Create(Verdict.Free, "Free here.")));

		ui.Spot.Status.Detail.Should().Be("Lost the GPS signal. Waiting for a fresh position…");
		ui.Spot.IsChecking.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a check that finishes after the loss of the connection does not bring back an old "free" answer.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task HandleConnectivityChange_CheckFinishesAfterOffline_ShouldNotOverwriteCantVerify()
	{
		using var ui = new UiFixture();
		var pending = HoldChecks(ui);
		var check = ui.Spot.HandleMapClickAsync(UiFixture.Here);

		await ui.Spot.HandleConnectivityChangeAsync(false);
		pending.SetResult(Result.Success(SpotDecision.Create(Verdict.Free, "Free here.")));
		await check;

		ui.Spot.Status.Detail.Should().Be("Internet connection lost. The app only answers with live data.");
		ui.Spot.IsChecking.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a check that finishes after a failed location does not bring back an old "free" answer.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task LocationError_CheckFinishesAfterError_ShouldNotOverwriteCantVerify()
	{
		using var ui = new UiFixture();
		var pending = HoldChecks(ui);
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());

		ui.RaiseLocationError("no signal");
		pending.SetResult(Result.Success(SpotDecision.Create(Verdict.Free, "Free here.")));

		ui.Spot.Status.Detail.Should().Be("No GPS position: no signal. Allow location for this app, or tap the map.");
		ui.Spot.IsChecking.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the answer of an older check does not replace the answer of a newer one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task HandlePinMoved_OlderCheckAnswersLast_ShouldBeDropped()
	{
		using var ui = new UiFixture();
		var older = new TaskCompletionSource<Result<SpotDecision>>();
		var newer = new TaskCompletionSource<Result<SpotDecision>>();
		ui.Sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).Returns(older.Task, newer.Task);

		var first = ui.Spot.HandlePinMovedAsync(UiFixture.Here);
		var second = ui.Spot.HandlePinMovedAsync(UiFixture.Here);
		newer.SetResult(Result.Success(SpotDecision.Create(Verdict.Pay, "New answer.")));
		older.SetResult(Result.Success(SpotDecision.Create(Verdict.Free, "Old answer.")));
		await Task.WhenAll(first, second);

		ui.Spot.Status.Detail.Should().Be("New answer.");
	}
	#endregion

	#region Background Failure Tests
	/// <summary>
	/// Verifies that a check started by a fix that throws is told to the user as "can't verify" with the message.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FixReceived_CheckThrows_ShouldSayCantVerifyWithTheMessage()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
		await ui.Spot.StartTrackingAsync();

		ui.RaiseFix(ui.FixNow());

		ui.Spot.Status.Title.Should().Be("Can't verify right now");
		ui.Spot.Status.Detail.Should().Be("Unexpected error: boom");
		ui.Spot.IsChecking.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a check started by the timer that throws is told to the user instead of being lost.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Tick_RecheckThrows_ShouldSayCantVerifyWithTheMessage()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());
		ui.Clock.Advance(TimeSpan.FromSeconds(25));
		ui.RaiseFix(ui.FixNow());
		ui.Sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

		ui.Clock.Advance(TimeSpan.FromSeconds(5));

		ui.Spot.Status.Detail.Should().Be("Unexpected error: boom");
	}

	/// <summary>
	/// Verifies that a cancellation that is not caused by the screen going away is told to the user.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FixReceived_CheckCancelledWhileAlive_ShouldSayCantVerify()
	{
		using var ui = new UiFixture();
		var pending = HoldChecks(ui);
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());

		pending.SetCanceled();
		SpinWait.SpinUntil(() => !ui.Spot.IsChecking, TimeSpan.FromSeconds(5)).Should().BeTrue("a cancelled task runs its continuation on the thread pool");

		ui.Spot.Status.Title.Should().Be("Can't verify right now");
	}

	/// <summary>
	/// Verifies that a cancellation caused by the screen going away is ignored and shows nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Dispose_CheckCancelled_ShouldShowNothing()
	{
		using var ui = new UiFixture();
		var pending = HoldChecks(ui);
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());

		ui.Spot.Dispose();
		pending.SetCanceled();

		ui.Spot.Status.Title.Should().NotBe("Can't verify right now");
	}

	/// <summary>
	/// Verifies that a tick or a second disposal after the screen went away does not throw.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Dispose_ThenTickAndDisposeAgain_ShouldNotThrow()
	{
		using var ui = new UiFixture();
		await ui.Spot.StartTrackingAsync();
		ui.Spot.Dispose();

		var act = async () =>
		{
			await ui.Spot.TickAsync().ConfigureAwait(false);
			ui.Spot.Dispose();
		};

		await act.Should().NotThrowAsync();
	}
	#endregion

	#region Pin Tests
	/// <summary>
	/// Verifies that a tap on the map stops the following, drops the pin and checks it without an accuracy.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task HandleMapClick_WhileFollowing_ShouldStopFollowingAndCheckThePin()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.StartTrackingAsync();

		await ui.Spot.HandleMapClickAsync(new GeoPoint(50.83, 4.36));

		ui.Spot.IsTracking.Should().BeFalse();
		ui.Spot.Pin.Should().Be(new GeoPoint(50.83, 4.36));
		await ui.Tracker.Received(1).StopAsync(Arg.Any<CancellationToken>());
		await ui.Sender.Received(1).Send(Arg.Is<DecideSpotQuery>(q => q.AccuracyMeters == null), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that starting to follow drops the pin.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task StartTracking_WithPin_ShouldDropThePin()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		await ui.Spot.HandleMapClickAsync(UiFixture.Here);

		await ui.Spot.StartTrackingAsync();

		ui.Spot.Pin.Should().BeNull();
		await ui.Tracker.Received(1).StartAsync(Arg.Any<CancellationToken>());
	}
	#endregion

	#region Nearest Spot Tests
	/// <summary>
	/// Verifies that a paid answer in a zone starts the nearest-spot search and a free answer hides it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_PaidInZoneThenFree_ShouldSearchThenHideTheNearestSpot()
	{
		using var ui = new UiFixture();
		ui.AnswerSearches(new FreeSpot(new GeoPoint(50.8252, 4.3608), 25, "Rue Test", "Ixelles", "Rue Test, Ixelles"));
		ui.AnswerChecks(UiFixture.InZone(Verdict.Pay));

		await ui.Spot.HandleMapClickAsync(UiFixture.Here);
		ui.Nearest.State.Should().Be(NearestSpotState.Found);

		ui.AnswerChecks(UiFixture.InZone(Verdict.Free));
		await ui.Spot.HandlePinMovedAsync(UiFixture.Here);

		ui.Nearest.State.Should().Be(NearestSpotState.Hidden);
	}

	/// <summary>
	/// Verifies that a change from covered to not covered while following is announced once.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_StatusChangesWhileFollowing_ShouldAnnounceTheChange()
	{
		using var ui = new UiFixture();
		ui.AnswerSearches();
		ui.AnswerChecks(UiFixture.InZone(Verdict.Free));
		await ui.Spot.StartTrackingAsync();
		ui.RaiseFix(ui.FixNow());
		ui.Toast.Message.Should().BeEmpty();

		ui.AnswerChecks(UiFixture.InZone(Verdict.Pay));
		ui.RaiseFix(ui.FixNow(new GeoPoint(UiFixture.Here.Latitude + 0.0003, UiFixture.Here.Longitude)));

		ui.Toast.Message.Should().Be("🔴 Not covered here");
	}

	/// <summary>
	/// Verifies that "Find free parking" without any position asks the user to tap the map and starts following.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FindFreeParking_NoPosition_ShouldAskForOneAndStartFollowing()
	{
		using var ui = new UiFixture();

		await ui.Spot.FindFreeParkingAsync();

		ui.Toast.Message.Should().Be("Turn on my location, or tap the map first");
		ui.Spot.IsTracking.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that "Find free parking" with a pin searches around it and asks the map to show both places.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FindFreeParking_WithPin_ShouldSearchAndAskToFitBothPlaces()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(SpotDecision.Create(Verdict.Pay, "x"));
		var best = new FreeSpot(new GeoPoint(50.8252, 4.3608), 25, "Rue Test", "Ixelles", "Rue Test, Ixelles");
		ui.AnswerSearches(best);
		await ui.Spot.HandleMapClickAsync(UiFixture.Here);
		MapFitRequest? fit = null;
		ui.Spot.FitRequested += (_, e) => fit = e.Value;

		await ui.Spot.FindFreeParkingAsync();

		fit.Should().Be(new MapFitRequest(UiFixture.Here, best.Point));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Makes every spot check wait until the test answers it.
	/// </summary>
	/// <param name="ui">The fixture whose mediator holds the checks.</param>
	/// <returns>The source that completes the held checks.</returns>
	private static TaskCompletionSource<Result<SpotDecision>> HoldChecks(UiFixture ui)
	{
		var pending = new TaskCompletionSource<Result<SpotDecision>>();
		ui.Sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
		return pending;
	}
	#endregion
}
