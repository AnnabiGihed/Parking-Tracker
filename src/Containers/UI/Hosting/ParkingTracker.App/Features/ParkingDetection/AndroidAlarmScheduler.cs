using Android.App;
using Android.Content;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using AndroidUri = Android.Net.Uri;

namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="IAlarmScheduler"/> of the phone over <see cref="AlarmManager"/> (ADR-0002): a reminder is an exact alarm
///              that also fires in doze mode (<c>SetExactAndAllowWhileIdle</c>) when the app may schedule exact alarms, and an inexact
///              one (<c>SetAndAllowWhileIdle</c>) otherwise, which the system may delay by some minutes. Each alarm broadcasts to
///              <see cref="TimerAlarmReceiver"/> with a request code equal to the reminder id, so that scheduling again replaces it
///              and cancelling needs only the ids. Alarms do not survive a restart of the phone.
/// </summary>
/// <remarks>
/// This class depends on Android, so it cannot be unit-tested on a PC; the emulator test covers it (docs/architecture.md, section 6.3).
/// </remarks>
public sealed class AndroidAlarmScheduler : IAlarmScheduler
{
	#region Static Instances
	/// <summary>
	/// The ids of the reminders a timer can have.
	/// </summary>
	private static readonly int[] ReminderIds = [ParkingTimer.WarningId, ParkingTimer.EndId];
	#endregion

	#region IAlarmScheduler Implementation
	/// <inheritdoc />
	public Task<AlarmPrecision> ScheduleAsync(IReadOnlyList<TimerReminder> reminders, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reminders);
		if (reminders.Count == 0)
		{
			return Task.FromResult(AlarmPrecision.Exact);
		}

		var alarms = GetAlarmManager();
		var exact = CanScheduleExact(alarms);
		foreach (var reminder in reminders)
		{
			var pending = BuildPendingIntent(reminder.Id, reminder.Body);
			var at = reminder.At.ToUnixTimeMilliseconds();
			if (exact)
			{
				exact = TrySetExact(alarms, at, pending);
			}

			if (!exact)
			{
				alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
			}
		}

		return Task.FromResult(exact ? AlarmPrecision.Exact : AlarmPrecision.Inexact);
	}

	/// <inheritdoc />
	public Task CancelAsync(CancellationToken cancellationToken = default)
	{
		var alarms = GetAlarmManager();
		foreach (var id in ReminderIds)
		{
			alarms.Cancel(BuildPendingIntent(id, string.Empty));
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task<bool> CanScheduleExactAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(CanScheduleExact(GetAlarmManager()));
	}

	/// <inheritdoc />
	public Task OpenExactAlarmSettingsAsync(CancellationToken cancellationToken = default)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(31))
		{
			return Task.CompletedTask;
		}

		var context = Platform.AppContext;
		var intent = new Intent(Settings.ActionRequestScheduleExactAlarm, AndroidUri.Parse("package:" + context.PackageName));
		intent.AddFlags(ActivityFlags.NewTask);
		context.StartActivity(intent);
		return Task.CompletedTask;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Gets the alarm manager of the phone.
	/// </summary>
	/// <returns>The alarm manager.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the phone has no alarm service.</exception>
	private static AlarmManager GetAlarmManager()
	{
		return Platform.AppContext.GetSystemService(Context.AlarmService) as AlarmManager
			?? throw new InvalidOperationException("The phone has no alarm service.");
	}

	/// <summary>
	/// Tells whether exact alarms are allowed: always before Android 12, a permission the user grants from there on.
	/// </summary>
	/// <param name="alarms">The alarm manager.</param>
	/// <returns><see langword="true"/> when exact alarms are allowed.</returns>
	private static bool CanScheduleExact(AlarmManager alarms)
	{
		return !OperatingSystem.IsAndroidVersionAtLeast(31) || alarms.CanScheduleExactAlarms();
	}

	/// <summary>
	/// Sets an exact alarm; a refusal (the user withdrew the permission meanwhile) is not an error but a reason to fall back.
	/// </summary>
	/// <param name="alarms">The alarm manager.</param>
	/// <param name="at">The moment in milliseconds since 1970.</param>
	/// <param name="pending">What the alarm sends.</param>
	/// <returns><see langword="true"/> when the exact alarm was set; <see langword="false"/> when the system refused it.</returns>
	private static bool TrySetExact(AlarmManager alarms, long at, PendingIntent pending)
	{
		try
		{
			alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
			return true;
		}
		catch (Java.Lang.SecurityException)
		{
			return false;
		}
	}

	/// <summary>
	/// Builds what an alarm sends: a broadcast to the receiver, with the reminder id as request code so that each reminder has its own.
	/// </summary>
	/// <param name="id">The id of the reminder.</param>
	/// <param name="body">The text of the reminder.</param>
	/// <returns>The pending intent.</returns>
	private static PendingIntent BuildPendingIntent(int id, string body)
	{
		var context = Platform.AppContext;
		var intent = new Intent(context, typeof(TimerAlarmReceiver));
		intent.PutExtra(TimerAlarmReceiver.IdExtra, id);
		intent.PutExtra(TimerAlarmReceiver.BodyExtra, body);
		return PendingIntent.GetBroadcast(context, id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
	}
	#endregion
}
