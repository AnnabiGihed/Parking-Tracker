using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Application.Abstractions.Messaging;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer side of the main screen: runs a spot check through the Application, keeps only the answer of the newest
///              check, and holds what the status block shows (status, headline, chips). A "can't verify" shown by the screen
///              invalidates every running check, so that a slow answer can never replace it. Checks, timers and tracker events run
///              on different threads, so every field is read and written under one lock; events and toasts are raised outside it.
/// </summary>
public sealed class SpotChecker : IDisposable
{
	#region Fields
	/// <summary>
	/// Sends the checks to the Application.
	/// </summary>
	private readonly ISender sender;

	/// <summary>
	/// Clock for the age of checks and the time on the chips.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// Shows the messages of the screen.
	/// </summary>
	private readonly ToastViewModel toast;

	/// <summary>
	/// The nearest-spot block that follows a check.
	/// </summary>
	private readonly NearestSpotViewModel nearest;

	/// <summary>
	/// The card, whose line is shown again after each check.
	/// </summary>
	private readonly CardViewModel card;

	/// <summary>
	/// Tells the owner that something the screen shows has changed.
	/// </summary>
	private readonly Action changed;

	/// <summary>
	/// Cancels the work of the screen when it goes away.
	/// </summary>
	private readonly CancellationTokenSource lifetime = new();

	/// <summary>
	/// Guards every field below.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// Counts the checks, so that an older answer cannot replace a newer one or a "can't verify".
	/// </summary>
	private int checkSequence;

	/// <summary>
	/// The last check that started.
	/// </summary>
	private CheckRecord? lastCheck;

	/// <summary>
	/// The status of the last answer about the user's position, to tell the user when it changes.
	/// </summary>
	private SpotStatus? lastStatus;

	/// <summary>
	/// The answer the status block shows.
	/// </summary>
	private SpotStatusView status = SpotStatusView.Idle;

	/// <summary>
	/// The headline of the last decision.
	/// </summary>
	private string lastHeadline = string.Empty;

	/// <summary>
	/// The chips under the status block.
	/// </summary>
	private IReadOnlyList<StatusChip> chips = [];

	/// <summary>
	/// Whether a check is running.
	/// </summary>
	private bool isChecking;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SpotChecker"/> class.
	/// </summary>
	/// <param name="sender">Sends the checks to the Application.</param>
	/// <param name="clock">Clock for the age of checks and the time on the chips.</param>
	/// <param name="toast">Shows the messages of the screen.</param>
	/// <param name="nearest">The nearest-spot block that follows a check.</param>
	/// <param name="card">The card, whose line is shown again after each check.</param>
	/// <param name="changed">Tells the owner that something the screen shows has changed.</param>
	public SpotChecker(ISender sender, TimeProvider clock, ToastViewModel toast, NearestSpotViewModel nearest, CardViewModel card, Action changed)
	{
		this.sender = sender;
		this.clock = clock;
		this.toast = toast;
		this.nearest = nearest;
		this.card = card;
		this.changed = changed;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the answer the status block shows.
	/// </summary>
	public SpotStatusView Status
	{
		get
		{
			lock (gate)
			{
				return status;
			}
		}
	}

	/// <summary>
	/// Gets the headline of the last decision, as saved with a parked car; empty before the first check.
	/// </summary>
	public string LastHeadline
	{
		get
		{
			lock (gate)
			{
				return lastHeadline;
			}
		}
	}

	/// <summary>
	/// Gets the chips under the status block.
	/// </summary>
	public IReadOnlyList<StatusChip> Chips
	{
		get
		{
			lock (gate)
			{
				return chips;
			}
		}
	}

	/// <summary>
	/// Gets a value indicating whether a check is running.
	/// </summary>
	public bool IsChecking
	{
		get
		{
			lock (gate)
			{
				return isChecking;
			}
		}
	}

	/// <summary>
	/// Gets the last check that started, or <see langword="null"/> when there was none since the following started.
	/// </summary>
	public CheckRecord? LastCheck
	{
		get
		{
			lock (gate)
			{
				return lastCheck;
			}
		}
	}

	/// <summary>
	/// Gets the token that is cancelled when the screen goes away; a cancelled token once the checker was disposed.
	/// </summary>
	public CancellationToken LifetimeToken
	{
		get
		{
			try
			{
				return lifetime.Token;
			}
			catch (ObjectDisposedException)
			{
				return new CancellationToken(canceled: true);
			}
		}
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Forgets the last check and the last announced status, which starts a fresh follow of the user.
	/// </summary>
	public void ForgetHistory()
	{
		lock (gate)
		{
			lastStatus = null;
			lastCheck = null;
		}
	}

	/// <summary>
	/// Shows a busy chip with a text, for example while the first position is awaited.
	/// </summary>
	/// <param name="text">What the user is waiting for.</param>
	public void ShowBusy(string text)
	{
		lock (gate)
		{
			chips = [SpotStatusPresenter.Busy(text)];
		}

		changed();
	}

	/// <summary>
	/// Checks a place and shows the answer, unless a newer check or a "can't verify" came meanwhile.
	/// </summary>
	/// <param name="point">The place to check.</param>
	/// <param name="source">Whether the place is the user's position or a pin.</param>
	/// <param name="accuracyMetres">The accuracy of the position in metres, or <see langword="null"/> for a pin.</param>
	/// <returns>A task that completes when the answer was shown or dropped.</returns>
	public async Task RunAsync(GeoPoint point, CheckSource source, double? accuracyMetres)
	{
		int mine;
		lock (gate)
		{
			mine = Interlocked.Increment(ref checkSequence);
			lastCheck = new CheckRecord(point, clock.GetUtcNow(), source, accuracyMetres);
			isChecking = true;
			chips = [SpotStatusPresenter.Busy("Checking live…")];
		}

		changed();
		var decision = await DecideAsync(point, accuracyMetres).ConfigureAwait(false);
		string? announcement;
		lock (gate)
		{
			if (mine != Volatile.Read(ref checkSequence))
			{
				return;
			}

			isChecking = false;
			Apply(decision, source, accuracyMetres);
			announcement = Announcement(decision, source);
		}

		changed();
		if (announcement is not null)
		{
			toast.Show(announcement);
		}

		card.RefreshLine();
		FollowUp(decision, point, source);
	}

	/// <summary>
	/// Shows "can't verify" and invalidates every running check, so that a check finishing later does not replace it with a stale answer.
	/// </summary>
	/// <param name="detail">Why the place cannot be verified.</param>
	/// <param name="source">Whether the place is the user's position or a pin.</param>
	public void ShowCantVerify(string detail, CheckSource source)
	{
		lock (gate)
		{
			Interlocked.Increment(ref checkSequence);
			isChecking = false;
			Apply(SpotDecision.CantVerify(detail), source, null);
		}

		changed();
	}

	/// <summary>
	/// Shows "can't verify" with the message of a failure that nobody expected.
	/// </summary>
	/// <param name="exception">The failure.</param>
	/// <param name="source">Whether the place is the user's position or a pin.</param>
	public void ShowUnexpectedFailure(Exception exception, CheckSource source)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ShowCantVerify($"Unexpected error: {exception.Message}", source);
	}

	/// <summary>
	/// Awaits background work started by an event or a timer and keeps its failure from being lost. A cancellation is ignored only
	/// when the screen is going away; any other failure goes to <paramref name="reportFailure"/>.
	/// </summary>
	/// <param name="work">The work that was started.</param>
	/// <param name="reportFailure">Tells the user what failed, for example through "can't verify" or a toast.</param>
	/// <returns>A task that does not fault when <paramref name="work"/> does.</returns>
	public async Task ObserveAsync(Task work, Action<Exception> reportFailure)
	{
		ArgumentNullException.ThrowIfNull(work);
		ArgumentNullException.ThrowIfNull(reportFailure);
		try
		{
			await work.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
		{
			// The screen is going away: nobody is left to tell.
		}
		catch (Exception exception)
		{
			reportFailure(exception);
		}
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		try
		{
			lifetime.Cancel();
		}
		catch (ObjectDisposedException)
		{
			return;
		}

		lifetime.Dispose();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Asks the Application for the decision; a failed query becomes "can't verify" with the reason.
	/// </summary>
	/// <param name="point">The place to check.</param>
	/// <param name="accuracyMetres">The accuracy of the position in metres, or <see langword="null"/>.</param>
	/// <returns>The decision.</returns>
	private async Task<SpotDecision> DecideAsync(GeoPoint point, double? accuracyMetres)
	{
		var result = await sender.Send(new DecideSpotQuery(point.Latitude, point.Longitude, accuracyMetres), LifetimeToken).ConfigureAwait(false);
		return result.IsSuccess ? result.Value : SpotDecision.CantVerify($"Unexpected error: {result.Error.Message}");
	}

	/// <summary>
	/// Puts an answer on the status block and the chips. The caller holds the lock.
	/// </summary>
	/// <param name="decision">The decision to show.</param>
	/// <param name="source">Whether the place is the user's position or a pin.</param>
	/// <param name="accuracyMetres">The accuracy of the position in metres, or <see langword="null"/>.</param>
	private void Apply(SpotDecision decision, CheckSource source, double? accuracyMetres)
	{
		status = SpotStatusPresenter.Present(decision);
		lastHeadline = SpotStatusPresenter.HeadlineOf(decision);
		chips = SpotStatusPresenter.Chips(decision, source, accuracyMetres, clock.GetLocalNow());
	}

	/// <summary>
	/// Works out whether the user is told that the answer about their own position changed from covered to not covered or back,
	/// and remembers the status. The caller holds the lock.
	/// </summary>
	/// <param name="decision">The decision just shown.</param>
	/// <param name="source">Whether the place is the user's position or a pin.</param>
	/// <returns>The message for the toast, or <see langword="null"/> when nothing changed.</returns>
	private string? Announcement(SpotDecision decision, CheckSource source)
	{
		if (source != CheckSource.Me || decision.Status == SpotStatus.Unknown)
		{
			return null;
		}

		string? message = null;
		if (lastStatus is { } previous && previous != decision.Status)
		{
			message = decision.Status == SpotStatus.Free ? "🟢 You are now in a spot covered by your card" : "🔴 Not covered here";
		}

		lastStatus = decision.Status;
		return message;
	}

	/// <summary>
	/// Hides the nearest-spot block when the place is free, and otherwise searches the nearest spots when that is due.
	/// </summary>
	/// <param name="decision">The decision just shown.</param>
	/// <param name="point">The place that was checked.</param>
	/// <param name="source">Whether the place is the user's position or a pin.</param>
	private void FollowUp(SpotDecision decision, GeoPoint point, CheckSource source)
	{
		if (decision.Status == SpotStatus.Free)
		{
			nearest.Hide();
		}
		else if (decision.Zone is not null && nearest.IsSearchDue(point, source))
		{
			_ = ObserveAsync(nearest.ShowAsync(point, LifetimeToken), exception => toast.Show($"Could not search for free spots ({exception.Message})."));
		}
	}
	#endregion
}
