using Android.Content;
using Android.Gms.Location;
using Android.OS;
using Microsoft.Extensions.Logging;
using ParkingTracker.App.Features.SpotDecisions;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using AndroidLocation = Android.Locations.Location;

namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One run of the background service: asks the fused provider of Google Play services for high-accuracy positions (about
///              every second, at least 5 m apart, as the background watcher of the first version did), hands each position to the
///              <see cref="ParkingWatcher"/> of the Application layer as a fix, and runs the watcher's loop that looks for a stop every
///              15 seconds. It holds no decision; those are in the watcher and the detector. <see cref="Dispose"/> ends both.
/// </summary>
/// <remarks>
/// This class depends on Android and Google Play services, so it cannot be unit-tested on a PC; the emulator test covers it
/// (docs/architecture.md, section 6.3).
/// </remarks>
internal sealed partial class BackgroundLocationSession : IDisposable
{
	#region Constants
	/// <summary>
	/// The accuracy given to a position that reports none, large enough that a decision treats it as unreliable.
	/// </summary>
	private const double UnknownAccuracyMetres = 9999;

	/// <summary>
	/// The time between positions asked of the device, in milliseconds.
	/// </summary>
	private const long IntervalMilliseconds = 1000;

	/// <summary>
	/// The least distance in metres between two positions; a phone that stands still reports none, the watcher looks at the clock.
	/// </summary>
	private const float MinimumDistanceMetres = 5;
	#endregion

	#region Fields
	/// <summary>
	/// The watcher that receives the fixes and looks for a stop.
	/// </summary>
	private readonly ParkingWatcher watcher;

	/// <summary>
	/// The log, where the start, a failure and the end go.
	/// </summary>
	private readonly ILogger<BackgroundLocationSession> logger;

	/// <summary>
	/// The context of the service, which the provider client is made for.
	/// </summary>
	private readonly Context context;

	/// <summary>
	/// Ends the loop of the watcher.
	/// </summary>
	private readonly CancellationTokenSource cancellation = new();

	/// <summary>
	/// The callback registered with the provider, or <see langword="null"/> before the start and after the end.
	/// </summary>
	private FusedLocationCallback? callback;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="BackgroundLocationSession"/> class.
	/// </summary>
	/// <param name="watcher">The watcher that receives the fixes and looks for a stop.</param>
	/// <param name="logger">The log, where the start, a failure and the end go.</param>
	/// <param name="context">The context of the service.</param>
	public BackgroundLocationSession(ParkingWatcher watcher, ILogger<BackgroundLocationSession> logger, Context context)
	{
		ArgumentNullException.ThrowIfNull(watcher);
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(context);
		this.watcher = watcher;
		this.logger = logger;
		this.context = context;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Registers for positions and starts the loop. Call it on the main thread, as the service does.
	/// </summary>
	/// <returns>A task that completes when the provider accepted the request.</returns>
	/// <exception cref="Java.Lang.SecurityException">Thrown when the location permission is missing.</exception>
	public async Task StartAsync()
	{
		watcher.Reset();
		var registered = new FusedLocationCallback(OnLocation, _ => { });
		var request = new LocationRequest.Builder(Priority.PriorityHighAccuracy, IntervalMilliseconds)
			.SetMinUpdateIntervalMillis(IntervalMilliseconds)
			.SetMinUpdateDistanceMeters(MinimumDistanceMetres)
			.Build();
		await LocationServices.GetFusedLocationProviderClient(context).RequestLocationUpdatesAsync(request, registered, Looper.MainLooper!).ConfigureAwait(false);
		callback = registered;
		// The loop ends by itself when the token is cancelled in Dispose; it logs its own failures.
		_ = Task.Run(() => watcher.RunAsync(cancellation.Token));
		LogStarted(logger);
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		cancellation.Cancel();
		if (callback is { } registered)
		{
			callback = null;
			_ = RemoveUpdatesAsync(registered);
		}

		cancellation.Dispose();
		LogStopped(logger);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Passes a position of the device on to the watcher as a fix.
	/// </summary>
	/// <param name="location">The position.</param>
	private void OnLocation(AndroidLocation location)
	{
		double? speed = location.HasSpeed ? location.Speed : null;
		var accuracy = location.HasAccuracy ? location.Accuracy : UnknownAccuracyMetres;
		var time = DateTimeOffset.FromUnixTimeMilliseconds(location.Time);
		watcher.Observe(new GpsFix(new GeoPoint(location.Latitude, location.Longitude), accuracy, speed, time));
	}

	/// <summary>
	/// Removes the callback from the provider; a failure is logged, because the service is ending anyway.
	/// </summary>
	/// <param name="registered">The callback to remove.</param>
	/// <returns>A task that completes when the provider no longer calls the callback.</returns>
	private async Task RemoveUpdatesAsync(FusedLocationCallback registered)
	{
		try
		{
			await LocationServices.GetFusedLocationProviderClient(context).RemoveLocationUpdatesAsync(registered).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogRemoveFailed(logger, exception);
		}
		finally
		{
			registered.Dispose();
		}
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that the background positions started.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Background location started for the parking alerts")]
	private static partial void LogStarted(ILogger logger);

	/// <summary>
	/// Logs that the background positions stopped.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Background location stopped")]
	private static partial void LogStopped(ILogger logger);

	/// <summary>
	/// Logs that removing the updates from the provider failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">The cause.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Background location updates could not be removed")]
	private static partial void LogRemoveFailed(ILogger logger, Exception exception);
	#endregion
}
