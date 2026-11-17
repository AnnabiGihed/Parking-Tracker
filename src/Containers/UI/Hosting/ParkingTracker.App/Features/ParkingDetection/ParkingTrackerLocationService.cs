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
///              the permission gone, or refuses to let it run in the foreground, it logs that and stops itself. Starting and
///              stopping it is the job of <see cref="AndroidParkingWatchService"/>.
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
	/// Records whether the service exists, from <see cref="OnCreate"/> to <see cref="OnDestroy"/>; <see langword="null"/> when the
	/// root provider is not there.
	/// </summary>
	private ParkingServiceState? state;

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
	/// The log of the service, taken from the root provider when the service is created.
	/// </summary>
	private ILogger<ParkingTrackerLocationService>? logger;
	#endregion

	#region Overrides
	/// <inheritdoc />
	public override void OnCreate()
	{
		base.OnCreate();
		var services = IPlatformApplication.Current?.Services;
		logger = services?.GetService<ILogger<ParkingTrackerLocationService>>();
		state = services?.GetService<ParkingServiceState>();
		state?.MarkCreated();
	}

	/// <inheritdoc />
	public override IBinder? OnBind(Intent? intent) => null;

	/// <inheritdoc />
	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		// The permanent notice must be up within seconds of the start command, before anything else is done.
		if (!TryStartForeground())
		{
			StopSelf();
			return StartCommandResult.NotSticky;
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
		state?.MarkDestroyed();
		base.OnDestroy();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Shows the permanent notice and makes the service a foreground service of type <c>location</c>. The system refuses that in
	/// some cases: since Android 12 when the app is in the background and has no exemption, and when the location permission is
	/// missing; both are logged and reported by the result, so that the caller can stop the service instead of crashing.
	/// </summary>
	/// <returns><see langword="true"/> when the service is in the foreground; <see langword="false"/> when the system refused.</returns>
	private bool TryStartForeground()
	{
		try
		{
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

			return true;
		}
		catch (Java.Lang.IllegalStateException exception) when (OperatingSystem.IsAndroidVersionAtLeast(31) && exception is ForegroundServiceStartNotAllowedException)
		{
			ReportForegroundRefused(exception);
			return false;
		}
		catch (Java.Lang.SecurityException exception)
		{
			ReportForegroundRefused(exception);
			return false;
		}
	}

	/// <summary>
	/// Starts the run, unless the alerts are off or the location is not allowed all the time any more (the system may have restarted
	/// the service after the user changed that), and stops the service when it cannot run.
	/// </summary>
	/// <returns>A task that completes when the run started or the service was asked to stop.</returns>
	private async Task StartSessionAsync()
	{
		var services = IPlatformApplication.Current?.Services;
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
			if (state is not { IsRunning: true })
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
	/// Logs the refusal of the foreground start when the log is there.
	/// </summary>
	/// <param name="exception">The refusal.</param>
	private void ReportForegroundRefused(Exception exception)
	{
		if (logger is not null)
		{
			LogForegroundRefused(logger, exception);
		}
	}

	/// <summary>
	/// Logs that the system refused to make the service a foreground service.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">The cause: the start is not allowed from the background, or the location permission is missing.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The system refused the foreground start of the parking alert service, which stops")]
	private static partial void LogForegroundRefused(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that the run could not start, so that the service stopped itself.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">The cause, for example a missing location permission.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The parking alert service could not start and stops")]
	private static partial void LogStartFailed(ILogger logger, Exception exception);
	#endregion
}
