using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using ParkingTracker.Application.Features.ParkingDetection.Services;

namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The foreground service of type <c>location</c> (ADR-0002) that keeps the app alive while it is off screen so that
///              parking is noticed: it shows the small permanent notice, then lets a <see cref="BackgroundLocationSession"/> follow the
///              position for the <see cref="ParkingWatcher"/>. It runs in the process of the app but outside the web view, so it takes
///              its services from the root provider of the app. It is not exported. When the system restarts it with the alerts off or
///              the permission gone, it stops itself. Starting and stopping it is the job of <see cref="AndroidParkingWatchService"/>.
/// </summary>
/// <remarks>
/// This class depends on Android and Google Play services, so it cannot be unit-tested on a PC; the emulator test covers it
/// (docs/architecture.md, section 6.3).
/// </remarks>
[Service(Name = "be.parkingtracker.app.ParkingTrackerLocationService", Exported = false, Enabled = true, ForegroundServiceType = ForegroundService.TypeLocation)]
public sealed partial class ParkingTrackerLocationService : Service
{
	#region Fields
	/// <summary>
	/// Whether an instance of the service exists in this process; set between <see cref="OnCreate"/> and <see cref="OnDestroy"/>.
	/// </summary>
	private static volatile bool created;

	/// <summary>
	/// 0 or 1: whether a run was already requested by an earlier start command, changed with <see cref="Interlocked"/>, so that a second
	/// start command does not start a second run.
	/// </summary>
	private int sessionRequested;

	/// <summary>
	/// The current run, or <see langword="null"/> before the first start command and after the end.
	/// </summary>
	private BackgroundLocationSession? session;

	/// <summary>
	/// The log of the service, taken from the root provider when the service starts.
	/// </summary>
	private ILogger<ParkingTrackerLocationService>? logger;
	#endregion

	#region Properties
	/// <summary>
	/// Gets a value indicating whether the service exists in this process, that is, whether it was started and has not ended.
	/// </summary>
	public static bool IsRunning => created;
	#endregion

	#region Overrides
	/// <inheritdoc />
	public override void OnCreate()
	{
		base.OnCreate();
		created = true;
	}

	/// <inheritdoc />
	public override IBinder? OnBind(Intent? intent) => null;

	/// <inheritdoc />
	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		// The permanent notice must be up within seconds of the start command, before anything else is done.
		ParkingNotifications.EnsureChannels(this);
		var notice = ParkingNotifications.BuildWatchNotice(this);
		if (OperatingSystem.IsAndroidVersionAtLeast(29))
		{
			StartForeground(ParkingNotifications.WatchNotificationId, notice, ForegroundService.TypeLocation);
		}
		else
		{
			StartForeground(ParkingNotifications.WatchNotificationId, notice);
		}

		if (Interlocked.Exchange(ref sessionRequested, 1) == 0)
		{
			_ = StartSessionAsync();
		}

		return StartCommandResult.Sticky;
	}

	/// <inheritdoc />
	public override void OnDestroy()
	{
		session?.Dispose();
		session = null;
		created = false;
		base.OnDestroy();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Starts the run, unless the alerts are off or the location is not allowed all the time any more (the system may have restarted
	/// the service after the user changed that), and stops the service when it cannot run.
	/// </summary>
	/// <returns>A task that completes when the run started or the service was asked to stop.</returns>
	private async Task StartSessionAsync()
	{
		var services = IPlatformApplication.Current?.Services;
		logger = services?.GetService<ILogger<ParkingTrackerLocationService>>();
		try
		{
			if (services is null || !await services.GetRequiredService<IParkingAlertCoordinator>().ShouldRunAsync().ConfigureAwait(false))
			{
				StopSelf();
				return;
			}

			var started = new BackgroundLocationSession(services.GetRequiredService<ParkingWatcher>(), services.GetRequiredService<ILogger<BackgroundLocationSession>>(), this);
			await MainThread.InvokeOnMainThreadAsync(() => started.StartAsync()).ConfigureAwait(false);
			session = started;
			if (!created)
			{
				// The service ended while the run was starting: nobody else will dispose it.
				started.Dispose();
				session = null;
			}
		}
		catch (Exception exception)
		{
			if (logger is not null)
			{
				LogStartFailed(logger, exception);
			}

			StopSelf();
		}
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that the run could not start, so that the service stopped itself.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">The cause, for example a missing location permission.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The parking alert service could not start and stops")]
	private static partial void LogStartFailed(ILogger logger, Exception exception);
	#endregion
}
