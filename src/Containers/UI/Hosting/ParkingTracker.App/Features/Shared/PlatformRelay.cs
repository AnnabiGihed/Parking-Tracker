using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ParkingTracker.App.Features.SpotDecisions;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A component without markup that joins events of the phone to the view models. It is a component because the view
///              models live in the scope of the web view, which only components (and not the MAUI app) can reach. It answers a change
///              of the connection with <see cref="SpotCheckViewModel.HandleConnectivityChangeAsync"/>, a return to the app with
///              <see cref="LocationPermissionViewModel.RefreshAsync"/>, and a change between covered and not covered with a vibration.
///              In a build made with <c>EnableTestHooks</c> it also registers <c>window.ptTest</c> (see the <c>TestHooks</c> partial file).
/// </summary>
public sealed partial class PlatformRelay : ComponentBase, IDisposable
{
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
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		Network.OnlineChanged -= OnOnlineChanged;
		Resume.Resumed -= OnResumed;
		Spot.CoverageChanged -= OnCoverageChanged;
#if TEST_HOOKS
		DisposeTestHooks();
#endif
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
	/// Reads the location permission again after the user came back to the app.
	/// </summary>
	/// <param name="sender">The notifier.</param>
	/// <param name="e">Nothing.</param>
	private async void OnResumed(object? sender, EventArgs e)
	{
		try
		{
			await Permission.RefreshAsync().ConfigureAwait(false);
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
	#endregion

	#region Logging
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
