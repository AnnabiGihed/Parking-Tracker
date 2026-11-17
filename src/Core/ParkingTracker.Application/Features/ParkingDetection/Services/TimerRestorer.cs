using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.Application.Features.ParkingDetection.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Schedules the reminders of the saved parking timer again. The phone forgets its alarms when it restarts, and it
///              cancels the exact ones when the user withdraws the permission for them (ADR-0002), so the host calls it after the
///              boot and after such a change. The timer itself is never changed or removed here, only its alarms are replaced.
/// </summary>
public sealed class TimerRestorer
{
	#region Fields
	/// <summary>
	/// The saved settings, which hold the timer.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// Schedules the reminders.
	/// </summary>
	private readonly IAlarmScheduler alarms;

	/// <summary>
	/// Clock that tells which reminders are still ahead.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="TimerRestorer"/> class.
	/// </summary>
	/// <param name="settings">The saved settings, which hold the timer.</param>
	/// <param name="alarms">Schedules the reminders.</param>
	/// <param name="clock">Clock that tells which reminders are still ahead.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public TimerRestorer(ISettingsStore settings, IAlarmScheduler alarms, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(alarms);
		ArgumentNullException.ThrowIfNull(clock);
		this.settings = settings;
		this.alarms = alarms;
		this.clock = clock;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Replaces the scheduled alarms by those of the saved timer that are still ahead: the old ones are cancelled first, so that
	/// nothing is doubled. Without a saved timer, or when its end has passed, nothing is scheduled.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with <see langword="true"/> when at least one reminder was scheduled.</returns>
	public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
	{
		var timer = await settings.GetTimerAsync().ConfigureAwait(false);
		if (timer is null)
		{
			return false;
		}

		var reminders = timer.Plan(clock.GetLocalNow());
		await alarms.CancelAsync(cancellationToken).ConfigureAwait(false);
		if (reminders.Count == 0)
		{
			return false;
		}

		await alarms.ScheduleAsync(reminders, cancellationToken).ConfigureAwait(false);
		return true;
	}
	#endregion
}
