using ParkingTracker.Domain.Features.Cars.ValueObjects;

namespace ParkingTracker.Application.Features.ParkingDetection.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the alarms of the phone that show the timer reminders while the app is closed: schedules the reminders of
///              a <see cref="ParkingTimer"/> as exact alarms when the app is allowed to, as inexact ones otherwise, cancels them, and
///              opens the system screen where the user allows exact alarms.
/// </summary>
public interface IAlarmScheduler
{
	#region Methods
	/// <summary>
	/// Schedules the reminders, replacing the ones scheduled before under the same ids.
	/// </summary>
	/// <param name="reminders">The reminders to show.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with how exactly the reminders will come; <see cref="AlarmPrecision.Exact"/> when there is nothing to schedule.</returns>
	Task<AlarmPrecision> ScheduleAsync(IReadOnlyList<TimerReminder> reminders, CancellationToken cancellationToken = default);

	/// <summary>
	/// Cancels the reminders of the timer; nothing happens when none is scheduled.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the alarms are cancelled.</returns>
	Task CancelAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Tells whether the app may schedule exact alarms.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with <see langword="true"/> when exact alarms are allowed.</returns>
	Task<bool> CanScheduleExactAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens the system screen where the user allows exact alarms for the app. Nothing happens on a system that has none.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the screen was opened.</returns>
	Task OpenExactAlarmSettingsAsync(CancellationToken cancellationToken = default);
	#endregion
}
