using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ParkingDetection.Services;

namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Receives the two system broadcasts after which the app must put its background work back (ADR-0002): the end of the
///              boot, when the phone has forgotten every alarm and the parking alert service, and the change of the permission for
///              exact alarms, after which the system cancels the exact alarms. It schedules the alarms of the saved parking timer
///              again through <see cref="TimerRestorer"/> and, after a boot, starts the parking alert service when the alerts are on
///              and the location is allowed all the time. It is not exported; the system can still call it for these two actions. It
///              holds no logic of its own.
/// </summary>
/// <remarks>
/// This class depends on Android, so it cannot be unit-tested on a PC; the logic is in <see cref="TimerRestorer"/> and
/// <see cref="ParkingAlertCoordinator"/>, which are tested (docs/architecture.md, section 6.3). Whether the system lets the app
/// start the service at the end of a boot depends on the version of Android; a refusal is logged and the service then starts
/// at the next launch of the app.
/// </remarks>
[BroadcastReceiver(Name = "be.parkingtracker.app.BootReceiver", Enabled = true, Exported = false)]
[IntentFilter(new[] { Intent.ActionBootCompleted, ExactAlarmPermissionChangedAction })]
public sealed partial class BootReceiver : BroadcastReceiver
{
	#region Constants
	/// <summary>
	/// The action the system broadcasts when the user allows exact alarms for the app
	/// (<c>AlarmManager.ACTION_SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED</c>, Android 12 and later), written out because the
	/// attribute is read when the project is built, whatever the lowest supported version.
	/// </summary>
	public const string ExactAlarmPermissionChangedAction = "android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED";
	#endregion

	#region Overrides
	/// <inheritdoc />
	public override void OnReceive(Context? context, Intent? intent)
	{
		var action = intent?.Action;
		var services = IPlatformApplication.Current?.Services;
		if (services is null || (action != Intent.ActionBootCompleted && action != ExactAlarmPermissionChangedAction))
		{
			return;
		}

		// The work is asynchronous, so the system is asked to keep the process until it has finished.
		var pending = GoAsync();
		_ = RestoreAsync(services, action == Intent.ActionBootCompleted, pending);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Schedules the alarms of the saved timer again and, after a boot, applies the switch of the parking alerts. A failure is
	/// logged; the broadcast is always finished.
	/// </summary>
	/// <param name="services">The root provider of the app.</param>
	/// <param name="afterBoot">Whether the phone has just started, in which case the alert service is started too.</param>
	/// <param name="pending">The handle that keeps the process alive; finished at the end.</param>
	/// <returns>A task that completes when the work ended.</returns>
	private static async Task RestoreAsync(IServiceProvider services, bool afterBoot, PendingResult? pending)
	{
		try
		{
			await services.GetRequiredService<TimerRestorer>().RestoreAsync().ConfigureAwait(false);
			if (afterBoot)
			{
				await services.GetRequiredService<IParkingAlertCoordinator>().ApplyAsync().ConfigureAwait(false);
			}
		}
		catch (Exception exception)
		{
			LogRestoreFailed(services.GetRequiredService<ILogger<BootReceiver>>(), exception);
		}
		finally
		{
			pending?.Finish();
		}
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that putting the alarms and the service back failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Restoring the timer alarms and the parking alert service failed")]
	private static partial void LogRestoreFailed(ILogger logger, Exception exception);
	#endregion
}
