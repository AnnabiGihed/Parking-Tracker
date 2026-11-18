using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ParkingTracker.App.Features.SpotDecisions;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A component without markup that joins events of the phone to the view models. It is a component because the view
///              models live in the scope of the web view, which only components (and not the MAUI app) can reach. It answers a change
///              of the connection with <see cref="SpotCheckViewModel.HandleConnectivityChangeAsync"/>, a return to the app with
///              <see cref="LocationPermissionViewModel.RefreshAsync"/>, and a change between covered and not covered with a
///              vibration. It also keeps the background service of the parking alerts in line with the switch and the permissions
///              (when the app opens and when the user comes back), asks for the notification permission a few seconds after the
///              start while the alerts are on, and reads the saved car again on a return, because the service saves it while the
///              app is away. A parking found while the app is on screen comes here instead of as a notification: it shows a
///              message and checks the place.
/// </summary>
public sealed partial class PlatformRelay : ComponentBase, IDisposable
{
	#region Static Instances
	/// <summary>
	/// How long after the start the notification permission is asked, so that it does not collide with the location question.
	/// </summary>
	private static readonly TimeSpan NotificationAskDelay = TimeSpan.FromSeconds(4);
	#endregion

	#region Injected Services
	/// <summary>
	/// Gets or sets the spot check, which answers a change of the connection and announces a change of coverage.
	/// </summary>
	[Inject]
	public SpotCheckViewModel Spot { get; set; } = null!;

	/// <summary>
	/// Gets or sets the location permission flow, which reads the permission again after the user came back.
	/// </summary>
	[Inject]
	public LocationPermissionViewModel Permission { get; set; } = null!;

	/// <summary>
	/// Gets or sets the connection status, which reports a change between online and offline.
	/// </summary>
	[Inject]
	public ConnectivityNetworkStatus Network { get; set; } = null!;

	/// <summary>
	/// Gets or sets the notifier of the "app resumed" moment.
	/// </summary>
	[Inject]
	public AppResumeNotifier Resume { get; set; } = null!;

	/// <summary>
	/// Gets or sets the vibrator.
	/// </summary>
	[Inject]
	public CoverageVibrator Vibrator { get; set; } = null!;

	/// <summary>
	/// Gets or sets the coordinator that starts and stops the background service of the parking alerts.
	/// </summary>
	[Inject]
	public IParkingAlertCoordinator Alerts { get; set; } = null!;

	/// <summary>
	/// Gets or sets the notifications of the phone, whose permission is asked while the alerts are on.
	/// </summary>
	[Inject]
	public INotifier Notifier { get; set; } = null!;

	/// <summary>
	/// Gets or sets the saved settings, which hold the alerts switch.
	/// </summary>
	[Inject]
	public ISettingsStore Settings { get; set; } = null!;

	/// <summary>
	/// Gets or sets the start-up gate (the migration of the saved data of the previous version and the device database), which
	/// finishes before the settings are read.
	/// </summary>
	[Inject]
	public StartupGate Gate { get; set; } = null!;

	/// <summary>
	/// Gets or sets the car dialog, whose saved car is read again after a return to the app.
	/// </summary>
	[Inject]
	public CarViewModel Car { get; set; } = null!;

	/// <summary>
	/// Gets or sets the watcher of the background service, which reports a parking found while the app is on screen.
	/// </summary>
	[Inject]
	public ParkingWatcher Watcher { get; set; } = null!;

	/// <summary>
	/// Gets or sets the reaction of the screen to a parking found while the app is on screen.
	/// </summary>
	[Inject]
	public ParkedOnScreenResponder ParkedResponder { get; set; } = null!;

	/// <summary>
	/// Gets or sets the clock that times the wait before the notification question.
	/// </summary>
	[Inject]
	public TimeProvider Clock { get; set; } = null!;

	/// <summary>
	/// Gets or sets the log, where a failure of one of these background reactions goes.
	/// </summary>
	[Inject]
	public ILogger<PlatformRelay> Logger { get; set; } = null!;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override void OnInitialized()
	{
		Network.OnlineChanged += OnOnlineChanged;
		Resume.Resumed += OnResumed;
		Spot.CoverageChanged += OnCoverageChanged;
		Watcher.ParkedOnScreen += OnParkedOnScreen;
		_ = StartAlertsAsync();
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		Network.OnlineChanged -= OnOnlineChanged;
		Resume.Resumed -= OnResumed;
		Spot.CoverageChanged -= OnCoverageChanged;
		Watcher.ParkedOnScreen -= OnParkedOnScreen;
	}
	#endregion

	#region Event Handlers
	/// <summary>
	/// Passes a change of the connection to the spot check.
	/// </summary>
	/// <param name="sender">The connection status.</param>
	/// <param name="e">Whether the device is online now.</param>
	private async void OnOnlineChanged(object? sender, ValueEventArgs<bool> e)
	{
		try
		{
			await Spot.HandleConnectivityChangeAsync(e.Value).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogConnectionChangeFailed(Logger, exception);
		}
	}

	/// <summary>
	/// Reads the location permission and the saved car again after the user came back to the app, and starts or stops the background
	/// service of the parking alerts to match, because the user may have allowed the location all the time in the settings.
	/// </summary>
	/// <param name="sender">The notifier.</param>
	/// <param name="e">Nothing.</param>
	private async void OnResumed(object? sender, EventArgs e)
	{
		try
		{
			await Permission.RefreshAsync().ConfigureAwait(false);
			await Car.LoadAsync().ConfigureAwait(false);
			await Alerts.ApplyAsync().ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogResumeFailed(Logger, exception);
		}
	}

	/// <summary>
	/// Vibrates when the user's position became covered or not covered.
	/// </summary>
	/// <param name="sender">The spot check.</param>
	/// <param name="e">Whether the position is covered now.</param>
	private async void OnCoverageChanged(object? sender, ValueEventArgs<bool> e)
	{
		try
		{
			await Vibrator.VibrateAsync(e.Value ? CoverageVibration.CoveredPattern : CoverageVibration.NotCoveredPattern).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogVibrationFailed(Logger, exception);
		}
	}

	/// <summary>
	/// Shows the message for a parking found while the app is on screen, reads the saved car again and checks the place, as the
	/// first version did instead of sending a notification.
	/// </summary>
	/// <param name="sender">The watcher.</param>
	/// <param name="e">The headline, the place and the accuracy.</param>
	private async void OnParkedOnScreen(object? sender, ParkedOnScreenEventArgs e)
	{
		try
		{
			await ParkedResponder.RespondAsync(e).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogParkedOnScreenFailed(Logger, exception);
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Asks for the notification permission a few seconds after the start (the location question comes first), while the alerts are
	/// on, as the first version did, then starts the background service when everything it needs is allowed.
	/// </summary>
	/// <returns>A task that completes when the service follows the switch.</returns>
	private async Task StartAlertsAsync()
	{
		try
		{
			await Gate.RunOnceAsync().ConfigureAwait(false);
			await Task.Delay(NotificationAskDelay, Clock).ConfigureAwait(false);
			if (await Settings.GetAlertsEnabledAsync().ConfigureAwait(false))
			{
				await Notifier.RequestPermissionAsync().ConfigureAwait(false);
			}

			await Alerts.ApplyAsync().ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogAlertsStartFailed(Logger, exception);
		}
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that showing a parking found while the app was on screen failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Showing a parking found while the app is on screen failed")]
	private static partial void LogParkedOnScreenFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that starting the parking alerts failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Starting the parking alerts failed")]
	private static partial void LogAlertsStartFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that handling a change of the connection failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Handling the change of the connection failed")]
	private static partial void LogConnectionChangeFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that reading the location permission after the resume failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Reading the location permission after resume failed")]
	private static partial void LogResumeFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that playing the vibration failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "Playing the vibration after a change of coverage failed")]
	private static partial void LogVibrationFailed(ILogger logger, Exception exception);
	#endregion
}
