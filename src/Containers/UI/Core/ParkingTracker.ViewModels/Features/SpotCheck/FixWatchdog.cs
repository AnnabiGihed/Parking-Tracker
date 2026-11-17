namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The periodic look of the main screen at the followed position. It owns the timer that fires every five seconds
///              and judges the staleness: after 30 seconds without a check the place is checked again, and when the last fix is
///              older than 15 seconds the signal counts as lost. A device that is standing still must therefore keep delivering fixes
///              (see <c>ILocationTracker</c>), or the screen reports a lost signal.
/// </summary>
public sealed class FixWatchdog : IDisposable
{
	#region Static Instances
	/// <summary>
	/// How often the watchdog looks whether a check is due although no fix arrived.
	/// </summary>
	public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
	#endregion

	#region Fields
	/// <summary>
	/// Clock that drives the timer.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// What to do each time the timer fires; it handles its own failures.
	/// </summary>
	private readonly Action onTick;

	/// <summary>
	/// Guards <see cref="timer"/> against a start that races with a disposal.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// The timer, or <see langword="null"/> before <see cref="Start"/> and after disposal.
	/// </summary>
	private ITimer? timer;

	/// <summary>
	/// Whether the watchdog was disposed, so that it never starts again.
	/// </summary>
	private bool disposed;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FixWatchdog"/> class.
	/// </summary>
	/// <param name="clock">Clock that drives the timer.</param>
	/// <param name="onTick">What to do each time the timer fires; it must handle its own failures.</param>
	public FixWatchdog(TimeProvider clock, Action onTick)
	{
		this.clock = clock;
		this.onTick = onTick;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Starts the timer. Starting a started watchdog, or a disposed one, does nothing.
	/// </summary>
	public void Start()
	{
		lock (gate)
		{
			if (disposed || timer is not null)
			{
				return;
			}

			timer = clock.CreateTimer(_ => onTick(), null, Interval, Interval);
		}
	}

	/// <summary>
	/// Judges what the periodic look should do.
	/// </summary>
	/// <param name="isTracking">Whether the position of the user is followed.</param>
	/// <param name="me">The last usable fix, or <see langword="null"/> before the first one.</param>
	/// <param name="lastCheck">The last check that started, or <see langword="null"/> when there was none.</param>
	/// <param name="now">The moment on the clock of the device.</param>
	/// <returns>
	/// <see cref="WatchdogVerdict.Quiet"/> unless the position is followed, a fix exists and the last check is 30 seconds old; then
	/// <see cref="WatchdogVerdict.SignalLost"/> when the fix is older than 15 seconds, otherwise <see cref="WatchdogVerdict.Recheck"/>.
	/// </returns>
	public static WatchdogVerdict Judge(bool isTracking, PositionFix? me, CheckRecord? lastCheck, DateTimeOffset now)
	{
		if (!isTracking || me is null || now - (lastCheck?.Time ?? DateTimeOffset.MinValue) < TrackingRules.RecheckInterval)
		{
			return WatchdogVerdict.Quiet;
		}

		return now - me.ReceivedAt > TrackingRules.MaxFixAge ? WatchdogVerdict.SignalLost : WatchdogVerdict.Recheck;
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		lock (gate)
		{
			disposed = true;
			timer?.Dispose();
			timer = null;
		}
	}
	#endregion
}
