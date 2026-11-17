using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Application.Abstractions.Messaging;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The heart of the main screen: follows the position of the user or a pin and decides, by the tracking rules, when
///              the spot is checked. The checks and the answer live in <see cref="SpotChecker"/>, the periodic look in
///              <see cref="FixWatchdog"/>. It says "can't verify" for a lost signal, a failed location or a lost connection, and
///              asks the screen to move the map through events. Tracker events and timers arrive on any thread, so the position,
///              the pin and the follow flag are guarded by one lock. It knows no map, no browser and no MAUI type.
/// </summary>
public sealed class SpotCheckViewModel : ViewModelBase, IDisposable
{
	#region Constants
	/// <summary>
	/// The zoom level of the map when the first fix arrives.
	/// </summary>
	private const int FirstFixZoom = 18;

	/// <summary>
	/// The closest zoom level kept when following starts with a known position.
	/// </summary>
	private const int FollowZoom = 17;
	#endregion

	#region Fields
	/// <summary>
	/// Delivers the fixes of the device.
	/// </summary>
	private readonly ILocationTracker tracker;

	/// <summary>
	/// Clock for the age of fixes.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// Shows the messages of the screen.
	/// </summary>
	private readonly ToastViewModel toast;

	/// <summary>
	/// The card, whose list status is refreshed by the periodic look.
	/// </summary>
	private readonly CardViewModel card;

	/// <summary>
	/// Runs the checks and holds the answer.
	/// </summary>
	private readonly SpotChecker checker;

	/// <summary>
	/// The timer and the staleness judgement of the followed position.
	/// </summary>
	private readonly FixWatchdog watchdog;

	/// <summary>
	/// Guards <see cref="isTracking"/>, <see cref="me"/> and <see cref="pin"/>, which tracker events, timers and the screen touch from different threads.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// Whether the user's position is followed.
	/// </summary>
	private bool isTracking;

	/// <summary>
	/// The last usable fix of the user.
	/// </summary>
	private PositionFix? me;

	/// <summary>
	/// The pin the user dropped.
	/// </summary>
	private GeoPoint? pin;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SpotCheckViewModel"/> class and starts listening to the tracker.
	/// </summary>
	/// <param name="sender">Sends the checks to the Application.</param>
	/// <param name="tracker">Delivers the fixes of the device.</param>
	/// <param name="clock">Clock for the age of fixes and checks.</param>
	/// <param name="toast">Shows the messages of the screen.</param>
	/// <param name="nearest">The nearest-spot block.</param>
	/// <param name="card">The card, whose line is refreshed after each check.</param>
	public SpotCheckViewModel(ISender sender, ILocationTracker tracker, TimeProvider clock, ToastViewModel toast, NearestSpotViewModel nearest, CardViewModel card)
	{
		this.tracker = tracker;
		this.clock = clock;
		this.toast = toast;
		this.card = card;
		Nearest = nearest;
		checker = new SpotChecker(sender, clock, toast, nearest, card, NotifyChanged);
		watchdog = new FixWatchdog(clock, OnTick);
		checker.CoverageChanged += (_, e) => CoverageChanged?.Invoke(this, e);
		tracker.FixReceived += OnFixReceived;
		tracker.ErrorOccurred += OnLocationError;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the nearest-spot block.
	/// </summary>
	public NearestSpotViewModel Nearest { get; }

	/// <summary>
	/// Gets a value indicating whether the user's position is followed.
	/// </summary>
	public bool IsTracking
	{
		get
		{
			lock (gate)
			{
				return isTracking;
			}
		}
	}

	/// <summary>
	/// Gets the last usable fix of the user, or <see langword="null"/> before the first one.
	/// </summary>
	public PositionFix? Me
	{
		get
		{
			lock (gate)
			{
				return me;
			}
		}
	}

	/// <summary>
	/// Gets the pin the user dropped, or <see langword="null"/> when there is none.
	/// </summary>
	public GeoPoint? Pin
	{
		get
		{
			lock (gate)
			{
				return pin;
			}
		}
	}

	/// <summary>
	/// Gets the answer the status block shows.
	/// </summary>
	public SpotStatusView Status => checker.Status;

	/// <summary>
	/// Gets the headline of the last decision, as saved with a parked car; empty before the first check.
	/// </summary>
	public string LastHeadline => checker.LastHeadline;

	/// <summary>
	/// Gets the chips under the status block.
	/// </summary>
	public IReadOnlyList<StatusChip> Chips => checker.Chips;

	/// <summary>
	/// Gets a value indicating whether a check is running.
	/// </summary>
	public bool IsChecking => checker.IsChecking;

	/// <summary>
	/// Gets the place a car or a search should start from: the user's position while followed, otherwise the pin, otherwise the last fix.
	/// </summary>
	public GeoPoint? CurrentPosition
	{
		get
		{
			lock (gate)
			{
				return isTracking && me is not null ? me.Fix.Point : pin ?? me?.Fix.Point;
			}
		}
	}
	#endregion

	#region Events
	/// <summary>
	/// Occurs when the map should move to a place.
	/// </summary>
	public event EventHandler<ValueEventArgs<MapViewRequest>>? ViewRequested;

	/// <summary>
	/// Occurs when the map should show two places at once.
	/// </summary>
	public event EventHandler<ValueEventArgs<MapFitRequest>>? FitRequested;

	/// <summary>
	/// Occurs when the position could not be read, so that the permission banner can be refreshed.
	/// </summary>
	public event EventHandler? LocationFailed;

	/// <summary>
	/// Occurs when the answer about the user's own position changed from not covered to covered or back; the flag is
	/// <see langword="true"/> when the position is covered now.
	/// </summary>
	public event EventHandler<ValueEventArgs<bool>>? CoverageChanged;
	#endregion

	#region Public Methods
	/// <summary>
	/// Starts following the position of the user: drops the pin, checks the last known position at once and starts the tracker.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the tracker accepted the start and the first check ended.</returns>
	public async Task StartTrackingAsync(CancellationToken cancellationToken = default)
	{
		PositionFix? known;
		lock (gate)
		{
			isTracking = true;
			pin = null;
			known = me;
		}

		checker.ForgetHistory();
		watchdog.Start();
		var check = Task.CompletedTask;
		if (known is { } position)
		{
			ViewRequested?.Invoke(this, new ValueEventArgs<MapViewRequest>(new MapViewRequest(position.Fix.Point, FollowZoom, OnlyZoomIn: true)));
			check = checker.RunAsync(position.Fix.Point, CheckSource.Me, position.Fix.AccuracyMetres);
		}
		else
		{
			checker.ShowBusy("Finding your position…");
		}

		NotifyChanged();
		await tracker.StartAsync(cancellationToken).ConfigureAwait(false);
		await check.ConfigureAwait(false);
	}

	/// <summary>
	/// Stops following the position of the user and stops the tracker.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the tracker stopped.</returns>
	public async Task StopTrackingAsync(CancellationToken cancellationToken = default)
	{
		lock (gate)
		{
			isTracking = false;
		}

		NotifyChanged();
		await tracker.StopAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Handles the button that follows the position: starts or stops the tracking.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the change was made.</returns>
	public Task ToggleTrackingAsync(CancellationToken cancellationToken = default)
	{
		return IsTracking ? StopTrackingAsync(cancellationToken) : StartTrackingAsync(cancellationToken);
	}

	/// <summary>
	/// Handles a tap on the map: stops following the user and drops the pin there.
	/// </summary>
	/// <param name="point">The place that was tapped.</param>
	/// <returns>A task that completes when the check ended.</returns>
	public async Task HandleMapClickAsync(GeoPoint point)
	{
		if (IsTracking)
		{
			await StopTrackingAsync(checker.LifetimeToken).ConfigureAwait(false);
		}

		await PlacePinAsync(point).ConfigureAwait(false);
	}

	/// <summary>
	/// Handles the end of a drag of the pin: checks the new place.
	/// </summary>
	/// <param name="point">The place the pin was dropped on.</param>
	/// <returns>A task that completes when the check ended.</returns>
	public Task HandlePinMovedAsync(GeoPoint point) => PlacePinAsync(point);

	/// <summary>
	/// Shows an address found by the search: stops following, moves the map close and drops the pin.
	/// </summary>
	/// <param name="point">The place the search found.</param>
	/// <returns>A task that completes when the check ended.</returns>
	public async Task ShowSearchResultAsync(GeoPoint point)
	{
		if (IsTracking)
		{
			await StopTrackingAsync(checker.LifetimeToken).ConfigureAwait(false);
		}

		ViewRequested?.Invoke(this, new ValueEventArgs<MapViewRequest>(new MapViewRequest(point, FirstFixZoom)));
		await PlacePinAsync(point).ConfigureAwait(false);
	}

	/// <summary>
	/// Handles "Find free parking": searches the nearest covered spots around the user or the pin and shows both on the map.
	/// When there is no place yet, it asks the user to tap the map and starts following.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the search ended.</returns>
	public async Task FindFreeParkingAsync(CancellationToken cancellationToken = default)
	{
		if (CurrentPosition is not { } from)
		{
			toast.Show("Turn on my location, or tap the map first");
			if (!IsTracking)
			{
				await StartTrackingAsync(cancellationToken).ConfigureAwait(false);
			}

			return;
		}

		var best = await Nearest.ShowAsync(from, cancellationToken).ConfigureAwait(false);
		if (best is not null)
		{
			FitRequested?.Invoke(this, new ValueEventArgs<MapFitRequest>(new MapFitRequest(from, best.Point)));
		}
	}

	/// <summary>
	/// Checks the place again after something that can change the answer, such as a new card.
	/// </summary>
	/// <returns>A task that completes when the check ended.</returns>
	public Task RecheckAsync()
	{
		PositionFix? position;
		GeoPoint? dropped;
		bool following;
		lock (gate)
		{
			following = isTracking;
			position = me;
			dropped = pin;
		}

		if (following && position is not null)
		{
			return checker.RunAsync(position.Fix.Point, CheckSource.Me, position.Fix.AccuracyMetres);
		}

		return dropped is { } place ? checker.RunAsync(place, CheckSource.Pin, null) : Task.CompletedTask;
	}

	/// <summary>
	/// Handles a change of the connection: without internet the screen says "can't verify", with it the place is checked again.
	/// </summary>
	/// <param name="isOnline">Whether the device is online now.</param>
	/// <returns>A task that completes when the new state was handled.</returns>
	public Task HandleConnectivityChangeAsync(bool isOnline)
	{
		if (isOnline)
		{
			toast.Show("Back online");
			return RecheckAsync();
		}

		checker.ShowCantVerify("Internet connection lost. The app only answers with live data.", IsTracking ? CheckSource.Me : CheckSource.Pin);
		Nearest.Hide();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Looks whether a check is due although no fix arrived, as the <see cref="FixWatchdog"/> judges: after 30 seconds without a check
	/// the place is checked again, and when the last fix is older than 15 seconds the answer says the signal is lost. It also
	/// refreshes the official-list status of the card line.
	/// </summary>
	/// <returns>A task that completes when the work ended.</returns>
	public async Task TickAsync()
	{
		await card.RefreshListIfStaleAsync(checker.LifetimeToken).ConfigureAwait(false);
		PositionFix? position;
		bool following;
		lock (gate)
		{
			following = isTracking;
			position = me;
		}

		switch (FixWatchdog.Judge(following, position, checker.LastCheck, clock.GetUtcNow()))
		{
			case WatchdogVerdict.SignalLost:
				checker.ShowCantVerify("Lost the GPS signal. Waiting for a fresh position…", CheckSource.Me);
				break;
			case WatchdogVerdict.Recheck:
				await checker.RunAsync(position!.Fix.Point, CheckSource.Me, position.Fix.AccuracyMetres).ConfigureAwait(false);
				break;
			case WatchdogVerdict.Quiet:
			default:
				break;
		}
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		tracker.FixReceived -= OnFixReceived;
		tracker.ErrorOccurred -= OnLocationError;
		watchdog.Dispose();
		checker.Dispose();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Runs the periodic look when the timer fires; a failure becomes "can't verify".
	/// </summary>
	private void OnTick()
	{
		_ = checker.ObserveAsync(TickAsync(), ShowUnexpectedFailure);
	}

	/// <summary>
	/// Handles a fix of the tracker, which can arrive on any thread.
	/// </summary>
	/// <param name="sender">The tracker.</param>
	/// <param name="e">The fix.</param>
	private void OnFixReceived(object? sender, LocationFixEventArgs e)
	{
		var now = clock.GetUtcNow();
		var fix = e.Fix;
		if (TrackingRules.IsStale(fix, now))
		{
			return;
		}

		bool first;
		bool following;
		lock (gate)
		{
			first = me is null;
			me = new PositionFix(fix, now);
			following = isTracking;
		}

		if (first || following)
		{
			ViewRequested?.Invoke(this, new ValueEventArgs<MapViewRequest>(new MapViewRequest(fix.Point, first ? FirstFixZoom : null)));
		}

		NotifyChanged();
		if (following && TrackingRules.ShouldRecheck(checker.LastCheck, fix, now))
		{
			_ = checker.ObserveAsync(checker.RunAsync(fix.Point, CheckSource.Me, fix.AccuracyMetres), ShowUnexpectedFailure);
		}
	}

	/// <summary>
	/// Handles a failure of the tracker: says so, unless a pin is shown. A stop also ends the following; an interruption keeps it,
	/// because fixes may follow.
	/// </summary>
	/// <param name="sender">The tracker.</param>
	/// <param name="e">The failure.</param>
	private void OnLocationError(object? sender, LocationErrorEventArgs e)
	{
		if (e.Kind == LocationErrorKind.Interrupted)
		{
			ShowInterruption();
			return;
		}

		var hasPin = HasPin();
		lock (gate)
		{
			isTracking = false;
		}

		if (hasPin)
		{
			toast.Show("No GPS: showing the pin you dropped");
		}
		else
		{
			checker.ShowCantVerify($"No GPS position: {e.Message}. Allow location for this app, or tap the map.", CheckSource.Me);
		}

		NotifyChanged();
		LocationFailed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Says that there is no position for now while the following goes on; nothing is said when a pin is shown.
	/// </summary>
	private void ShowInterruption()
	{
		if (!HasPin())
		{
			checker.ShowCantVerify("No GPS position right now: waiting for a fix.", CheckSource.Me);
		}
	}

	/// <summary>
	/// Tells whether the user dropped a pin that the screen shows instead of the position.
	/// </summary>
	/// <returns><see langword="true"/> when a pin is set.</returns>
	private bool HasPin()
	{
		lock (gate)
		{
			return pin is not null;
		}
	}

	/// <summary>
	/// Shows "can't verify" for a failure of background work.
	/// </summary>
	/// <param name="exception">The failure.</param>
	private void ShowUnexpectedFailure(Exception exception)
	{
		checker.ShowUnexpectedFailure(exception, IsTracking ? CheckSource.Me : CheckSource.Pin);
	}

	/// <summary>
	/// Drops the pin and checks the place.
	/// </summary>
	/// <param name="point">The place of the pin.</param>
	/// <returns>A task that completes when the check ended.</returns>
	private Task PlacePinAsync(GeoPoint point)
	{
		lock (gate)
		{
			pin = point;
		}

		NotifyChanged();
		return checker.RunAsync(point, CheckSource.Pin, null);
	}
	#endregion
}
