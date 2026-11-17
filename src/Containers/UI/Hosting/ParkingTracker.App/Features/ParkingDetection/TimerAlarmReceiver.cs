using Android.App;
using Android.Content;
using ParkingTracker.Domain.Features.Cars.ValueObjects;

namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Receives the alarm of a timer reminder and shows its notification, so that the reminder comes while the app is closed
///              (ADR-0002). It is not exported: only the alarm manager, through the pending intent of
///              <see cref="AndroidAlarmScheduler"/>, can call it. It holds no logic besides reading the id and text from the intent.
/// </summary>
/// <remarks>
/// This class depends on Android, so it cannot be unit-tested on a PC; the emulator test covers it (docs/architecture.md, section 6.3).
/// </remarks>
[BroadcastReceiver(Name = "be.parkingtracker.app.TimerAlarmReceiver", Enabled = true, Exported = false)]
public sealed class TimerAlarmReceiver : BroadcastReceiver
{
	#region Constants
	/// <summary>
	/// The name of the extra that carries the id of the reminder.
	/// </summary>
	public const string IdExtra = "reminderId";

	/// <summary>
	/// The name of the extra that carries the text of the reminder.
	/// </summary>
	public const string BodyExtra = "reminderBody";
	#endregion

	#region Overrides
	/// <inheritdoc />
	public override void OnReceive(Context? context, Intent? intent)
	{
		if (context is null || intent is null)
		{
			return;
		}

		var id = intent.GetIntExtra(IdExtra, ParkingTimer.EndId);
		var body = intent.GetStringExtra(BodyExtra) ?? ParkingTimer.EndText;
		ParkingNotifications.Show(context, id, ParkingNotifications.TimerChannelId, ParkingNotifications.TimerTitle, body);
	}
	#endregion
}
