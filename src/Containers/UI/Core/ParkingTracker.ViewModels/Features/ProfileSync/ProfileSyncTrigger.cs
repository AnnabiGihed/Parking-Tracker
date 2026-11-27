using ParkingTracker.Application.Features.ProfileSync.Abstractions;

namespace ParkingTracker.ViewModels.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Decides when the profile sync runs (ADR-0032), apart from the sign-in and the account sheet, which ask for it themselves.
///              A change of the user starts a sync two seconds after the last change, so that a burst of edits is uploaded once. The
///              host calls <see cref="RequestNow"/> when the app starts or comes back to the front and when the network returns. A sync
///              that cannot run (signed out, owner not verified) does nothing, so every trigger is safe to fire at any time.
/// </summary>
public sealed class ProfileSyncTrigger : IDisposable
{
	#region Constants
	/// <summary>
	/// The seconds a change waits for the next one before it is uploaded.
	/// </summary>
	public const int DebounceSeconds = 2;
	#endregion

	#region Fields
	/// <summary>
	/// The sync to start.
	/// </summary>
	private readonly IProfileSync sync;

	/// <summary>
	/// Raises the event that a change of the user was committed.
	/// </summary>
	private readonly IProfileChangeTracker tracker;

	/// <summary>
	/// The clock of the delay.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// Guards <see cref="pending"/>.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// Cancels the wait of the last change when a newer one arrives; <see langword="null"/> when none waits.
	/// </summary>
	private CancellationTokenSource? pending;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileSyncTrigger"/> class and starts listening for changes.
	/// </summary>
	/// <param name="sync">The sync to start.</param>
	/// <param name="tracker">Raises the event that a change of the user was committed.</param>
	/// <param name="clock">The clock of the delay.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ProfileSyncTrigger(IProfileSync sync, IProfileChangeTracker tracker, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(sync);
		ArgumentNullException.ThrowIfNull(tracker);
		ArgumentNullException.ThrowIfNull(clock);
		this.sync = sync;
		this.tracker = tracker;
		this.clock = clock;
		tracker.Changed += OnChanged;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Starts a sync now, for the start of the app, its return to the front and the return of the network.
	/// </summary>
	public void RequestNow() => sync.Request();
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		tracker.Changed -= OnChanged;
		lock (gate)
		{
			pending?.Cancel();
			pending?.Dispose();
			pending = null;
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Restarts the wait that ends in a sync.
	/// </summary>
	/// <param name="sender">The tracker.</param>
	/// <param name="e">Nothing.</param>
	private void OnChanged(object? sender, EventArgs e)
	{
		CancellationToken token;
		lock (gate)
		{
			pending?.Cancel();
			pending?.Dispose();
			pending = new CancellationTokenSource();
			token = pending.Token;
		}

		_ = WaitThenSyncAsync(token);
	}

	/// <summary>
	/// Waits for the quiet period and starts the sync, unless a newer change cancelled the wait.
	/// </summary>
	/// <param name="token">Cancelled by a newer change or by disposing.</param>
	/// <returns>A task that completes when the wait ended.</returns>
	private async Task WaitThenSyncAsync(CancellationToken token)
	{
		try
		{
			await Task.Delay(TimeSpan.FromSeconds(DebounceSeconds), clock, token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// A newer change took over the wait.
			return;
		}

		sync.Request();
	}
	#endregion
}
