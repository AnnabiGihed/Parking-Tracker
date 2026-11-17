using Android.App;
using Android.Content;
using AndroidX.Core.App;
using AndroidColor = Android.Graphics.Color;

namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Everything the host knows about the notifications of the app in one place: the three channels (the permanent notice of
///              the background service, the parking alerts and the timer reminders), the tap that opens the app, and the one way a
///              notification is built and shown. The ids and texts of the permanent notice are those of the background-location
///              plugin of the first version, so an update replaces its notice instead of adding a second one.
/// </summary>
/// <remarks>
/// This class depends on Android, so it cannot be unit-tested on a PC; the emulator test covers it (docs/architecture.md, section 6.3).
/// </remarks>
internal static class ParkingNotifications
{
	#region Constants
	/// <summary>
	/// The id of the channel of the permanent notice, the one the plugin of the first version used.
	/// </summary>
	public const string WatchChannelId = "com.equimaps.capacitor_background_geolocation";

	/// <summary>
	/// The id of the channel of the parking alerts.
	/// </summary>
	public const string AlertsChannelId = "parking_alerts";

	/// <summary>
	/// The id of the channel of the timer reminders.
	/// </summary>
	public const string TimerChannelId = "parking_timer";

	/// <summary>
	/// The id of the permanent notice, the one the plugin of the first version used.
	/// </summary>
	public const int WatchNotificationId = 28351;

	/// <summary>
	/// The first id of the parking alerts; the last three digits come from the clock, so that two alerts in a row both stay.
	/// </summary>
	public const int AlertIdBase = 4300;

	/// <summary>
	/// The title of the permanent notice and of the app in the timer reminders.
	/// </summary>
	public const string AppTitle = "Parking Tracker";

	/// <summary>
	/// The title of the timer reminders: a parking sign and the name of the app.
	/// </summary>
	public const string TimerTitle = "\U0001F17F️ Parking Tracker";

	/// <summary>
	/// The text of the permanent notice.
	/// </summary>
	public const string WatchText = "Watching where you park, to tell you if it is free or paid.";

	/// <summary>
	/// The colour of the notifications, the blue of the app.
	/// </summary>
	private const string BrandColour = "#1D4ED8";
	#endregion

	#region Public Methods
	/// <summary>
	/// Creates the three channels; creating an existing channel changes nothing, so it is safe to call each time.
	/// </summary>
	/// <param name="context">Any context of the app.</param>
	public static void EnsureChannels(Context context)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26) || context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
		{
			return;
		}

		var watch = new NotificationChannel(WatchChannelId, "Parking check in the background", NotificationImportance.Low);
		watch.EnableLights(false);
		watch.EnableVibration(false);
		watch.SetSound(null, null);
		manager.CreateNotificationChannel(watch);
		manager.CreateNotificationChannel(new NotificationChannel(AlertsChannelId, "Parking alerts", NotificationImportance.High));
		manager.CreateNotificationChannel(new NotificationChannel(TimerChannelId, "Parking timer", NotificationImportance.High));
	}

	/// <summary>
	/// Builds the permanent notice of the background service.
	/// </summary>
	/// <param name="context">Any context of the app.</param>
	/// <returns>The notification; a tap opens the app and it cannot be swiped away.</returns>
	public static Notification BuildWatchNotice(Context context)
	{
		return Build(context, WatchChannelId, AppTitle, WatchText, isOngoing: true);
	}

	/// <summary>
	/// Shows a notification; a tap opens the app and removes it.
	/// </summary>
	/// <param name="context">Any context of the app.</param>
	/// <param name="id">The id; showing the same id again replaces the notification.</param>
	/// <param name="channelId">The channel: <see cref="AlertsChannelId"/> or <see cref="TimerChannelId"/>.</param>
	/// <param name="title">The title.</param>
	/// <param name="body">The text under the title, shown in full.</param>
	public static void Show(Context context, int id, string channelId, string title, string body)
	{
		EnsureChannels(context);
		NotificationManagerCompat.From(context)!.Notify(id, Build(context, channelId, title, body, isOngoing: false));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a notification with the icon, colour and tap action of the app.
	/// </summary>
	/// <param name="context">Any context of the app.</param>
	/// <param name="channelId">The channel.</param>
	/// <param name="title">The title.</param>
	/// <param name="body">The text under the title.</param>
	/// <param name="isOngoing">Whether the notification stays until the service ends instead of being dismissed by a tap or a swipe.</param>
	/// <returns>The notification.</returns>
	private static Notification Build(Context context, string channelId, string title, string body, bool isOngoing)
	{
		var builder = new NotificationCompat.Builder(context, channelId)
			.SetContentTitle(title)!
			.SetContentText(body)!
			.SetStyle(new NotificationCompat.BigTextStyle().BigText(body))!
			.SetSmallIcon(Resource.Drawable.ic_stat_parking)!
			.SetColor(AndroidColor.ParseColor(BrandColour))!
			.SetContentIntent(OpenAppIntent(context))!
			.SetOngoing(isOngoing)!
			.SetAutoCancel(!isOngoing)!
			.SetPriority(isOngoing ? NotificationCompat.PriorityLow : NotificationCompat.PriorityHigh)!;
		return builder.Build()!;
	}

	/// <summary>
	/// Builds the tap action that brings the app to the front, or starts it.
	/// </summary>
	/// <param name="context">Any context of the app.</param>
	/// <returns>The pending intent of the launch activity.</returns>
	private static PendingIntent? OpenAppIntent(Context context)
	{
		var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
		return PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
	}
	#endregion
}
