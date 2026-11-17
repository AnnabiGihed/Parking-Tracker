using Android.Gms.Common;
using Android.Gms.Location;
using Android.OS;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using AndroidLocation = Android.Locations.Location;

namespace ParkingTracker.App.Features.SpotDecisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ILocationTracker"/> of the phone over the fused location provider of Google Play services (ADR-0002) in
///              the foreground: high accuracy, a fix about every second and no minimum distance, so that fixes keep coming while the
///              phone stands still (the screen reports a lost signal after 15 seconds without one). Fixes and interruptions arrive on the
///              main looper; a failed start is reported on the thread that started. Background tracking off screen is a later stage. When the permission is missing, Play services
///              are unavailable or the request fails, the tracker stops and says why through <see cref="ErrorOccurred"/>; a moment
///              without position (<see cref="LocationAvailability.IsLocationAvailable"/> false) is reported as an interruption and
///              the updates go on.
/// </summary>
/// <remarks>
/// This class depends on Android and Google Play services, so it cannot be unit-tested on a PC; the emulator test covers it
/// (docs/architecture.md, section 6.3).
/// </remarks>
public sealed partial class FusedLocationTracker : ILocationTracker
{
	#region Constants
	/// <summary>
	/// The accuracy given to a fix that reports none, large enough that the decision treats it as unreliable.
	/// </summary>
	private const double UnknownAccuracyMetres = 9999;

	/// <summary>
	/// The time between fixes asked of the device, in milliseconds; it is also the fastest rate, so that other apps cannot push it up.
	/// </summary>
	private const long IntervalMilliseconds = 1000;
	#endregion

	#region Fields
	/// <summary>
	/// The log, where the start, a failure and the first fix of a start go, so that a run without a position can be explained.
	/// </summary>
	private readonly ILogger<FusedLocationTracker> logger;

	/// <summary>
	/// The clock that gives the age of the first fix.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// Guards <see cref="running"/>, <see cref="generation"/> and <see cref="callback"/>.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// Whether the tracker was started and has not stopped.
	/// </summary>
	private bool running;

	/// <summary>
	/// Counts the starts and the stops, so that a start that finishes after a stop knows it was replaced and leaves nothing behind.
	/// </summary>
	private int generation;

	/// <summary>
	/// The callback registered with the provider for the current start, or <see langword="null"/> when none is.
	/// </summary>
	private FusedLocationCallback? callback;

	/// <summary>
	/// Whether the first fix of the current start was logged; 0 or 1, changed with <see cref="Interlocked"/> because fixes may arrive on any thread.
	/// </summary>
	private int firstFixLogged;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FusedLocationTracker"/> class.
	/// </summary>
	/// <param name="logger">The log, where the start, a failure and the first fix of a start go.</param>
	/// <param name="clock">The clock that gives the age of the first fix.</param>
	public FusedLocationTracker(ILogger<FusedLocationTracker> logger, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(clock);
		this.logger = logger;
		this.clock = clock;
	}
	#endregion

	#region Properties
	/// <inheritdoc />
	public bool IsRunning
	{
		get
		{
			lock (gate)
			{
				return running;
			}
		}
	}
	#endregion

	#region Events
	/// <inheritdoc />
	public event EventHandler<LocationFixEventArgs>? FixReceived;

	/// <inheritdoc />
	public event EventHandler<LocationErrorEventArgs>? ErrorOccurred;
	#endregion

	#region ILocationTracker Implementation
	/// <inheritdoc />
	/// <remarks>
	/// A refusal or any other failure resets <see cref="IsRunning"/> and is reported through <see cref="ErrorOccurred"/>.
	/// A cancelled start also cleans up, and then throws the <see cref="System.OperationCanceledException"/>.
	/// </remarks>
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		int attempt;
		lock (gate)
		{
			if (running)
			{
				return;
			}

			running = true;
			attempt = ++generation;
		}

		Volatile.Write(ref firstFixLogged, 0);
		LogStartRequested(logger);
		try
		{
			await RequestUpdatesAsync(attempt, cancellationToken).ConfigureAwait(false);
		}
		catch (System.OperationCanceledException)
		{
			await AbortAsync(attempt).ConfigureAwait(false);
			throw;
		}
		catch (Java.Lang.SecurityException exception)
		{
			Fail("location not allowed", attempt, exception);
		}
		catch (Exception exception)
		{
			Fail("location could not be started", attempt, exception);
		}
	}

	/// <inheritdoc />
	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		FusedLocationCallback? stopped;
		lock (gate)
		{
			if (!running)
			{
				return;
			}

			running = false;
			generation++;
			stopped = callback;
			callback = null;
		}

		await RemoveUpdatesAsync(stopped).ConfigureAwait(false);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Checks Play services and the permission, then registers for updates, and removes them again when a stop or a newer start came
	/// while it was starting.
	/// </summary>
	/// <param name="attempt">The generation of this start.</param>
	/// <param name="cancellationToken">Cancels the start.</param>
	/// <returns>A task that completes when the provider delivers, or when the start failed and was reported.</returns>
	private async Task RequestUpdatesAsync(int attempt, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(Platform.AppContext) != ConnectionResult.Success)
		{
			Fail("Google Play services are not available on this device", attempt);
			return;
		}

		// The permission flow of the screen asks the user; here it is only read, because the provider throws without it.
		var status = await MainThread.InvokeOnMainThreadAsync(Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>).ConfigureAwait(false);
		if (status != PermissionStatus.Granted)
		{
			Fail("location not allowed", attempt);
			return;
		}

		var registered = new FusedLocationCallback(location => OnLocation(attempt, location), isAvailable => OnAvailability(attempt, isAvailable));
		lock (gate)
		{
			if (!running || attempt != generation)
			{
				return;
			}

			callback = registered;
		}

		await MainThread.InvokeOnMainThreadAsync(() => RegisterAsync(registered)).ConfigureAwait(false);
		LogStarted(logger);

		// The provider delivers now: a cancel or a stop that came meanwhile must not leave it on without a listener.
		cancellationToken.ThrowIfCancellationRequested();
		if (!IsCurrent(attempt))
		{
			await RemoveUpdatesAsync(registered).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Registers a callback with the fused provider for the high-accuracy request of the tracker.
	/// </summary>
	/// <param name="registered">The callback to register.</param>
	/// <returns>A task that completes when the provider accepted the request.</returns>
	private static async Task RegisterAsync(FusedLocationCallback registered)
	{
		var request = new LocationRequest.Builder(Priority.PriorityHighAccuracy, IntervalMilliseconds)
			.SetMinUpdateIntervalMillis(IntervalMilliseconds)
			.SetMinUpdateDistanceMeters(0)
			.Build();
		var client = LocationServices.GetFusedLocationProviderClient(Platform.AppContext);
		await client.RequestLocationUpdatesAsync(request, registered, Looper.MainLooper!).ConfigureAwait(false);
	}

	/// <summary>
	/// Passes a position of the device on as a fix, unless a stop or a newer start replaced the start it belongs to.
	/// </summary>
	/// <param name="attempt">The start the callback was registered for.</param>
	/// <param name="location">The position.</param>
	private void OnLocation(int attempt, AndroidLocation location)
	{
		if (!IsCurrent(attempt))
		{
			return;
		}

		var time = DateTimeOffset.FromUnixTimeMilliseconds(location.Time);
		var accuracy = location.HasAccuracy ? location.Accuracy : UnknownAccuracyMetres;
		if (Interlocked.Exchange(ref firstFixLogged, 1) == 0 && logger.IsEnabled(LogLevel.Information))
		{
			var ageSeconds = Math.Round((clock.GetUtcNow() - time).TotalSeconds, 1);
			LogFirstFix(logger, accuracy, ageSeconds);
		}

		double? speed = location.HasSpeed ? location.Speed : null;
		var fix = new GpsFix(new GeoPoint(location.Latitude, location.Longitude), accuracy, speed, time);
		FixReceived?.Invoke(this, new LocationFixEventArgs(fix));
	}

	/// <summary>
	/// Reports that the device cannot deliver positions for now while the updates go on.
	/// </summary>
	/// <param name="attempt">The start the callback was registered for.</param>
	/// <param name="isAvailable">Whether the device can deliver positions right now.</param>
	private void OnAvailability(int attempt, bool isAvailable)
	{
		if (isAvailable || !IsCurrent(attempt))
		{
			return;
		}

		// A gap (before the first fix, a provider that toggles) is not the end: the provider keeps delivering when it is back.
		LogInterrupted(logger);
		ErrorOccurred?.Invoke(this, new LocationErrorEventArgs("no position available", LocationErrorKind.Interrupted));
	}

	/// <summary>
	/// Stops the tracker and tells the listeners why. Nothing happens when the tracker already stopped or when the start that
	/// failed was replaced by a newer one.
	/// </summary>
	/// <param name="message">What went wrong, in words for the user.</param>
	/// <param name="attempt">The start that failed.</param>
	/// <param name="exception">The cause, or <see langword="null"/> when there is no exception.</param>
	private void Fail(string message, int attempt, Exception? exception = null)
	{
		FusedLocationCallback? stopped;
		lock (gate)
		{
			if (!running || attempt != generation)
			{
				return;
			}

			running = false;
			generation++;
			stopped = callback;
			callback = null;
		}

		LogFailed(logger, message, exception);
		_ = RemoveUpdatesAsync(stopped);
		ErrorOccurred?.Invoke(this, new LocationErrorEventArgs(message, LocationErrorKind.Stopped));
	}

	/// <summary>
	/// Undoes a start that was cancelled: resets the flag when the start is still the current one, and removes its updates.
	/// </summary>
	/// <param name="attempt">The start that was cancelled.</param>
	/// <returns>A task that completes when the provider no longer delivers for this start.</returns>
	private async Task AbortAsync(int attempt)
	{
		FusedLocationCallback? stopped = null;
		lock (gate)
		{
			if (running && attempt == generation)
			{
				running = false;
				generation++;
				stopped = callback;
				callback = null;
			}
		}

		await RemoveUpdatesAsync(stopped).ConfigureAwait(false);
	}

	/// <summary>
	/// Removes a callback from the provider; a failure is logged, because the tracker is stopping anyway.
	/// </summary>
	/// <param name="stopped">The callback to remove, or <see langword="null"/> when none was registered.</param>
	/// <returns>A task that completes when the provider no longer calls the callback.</returns>
	private async Task RemoveUpdatesAsync(FusedLocationCallback? stopped)
	{
		if (stopped is null)
		{
			return;
		}

		try
		{
			await MainThread.InvokeOnMainThreadAsync(() => LocationServices.GetFusedLocationProviderClient(Platform.AppContext).RemoveLocationUpdatesAsync(stopped)).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogRemoveFailed(logger, exception);
		}
		finally
		{
			// The Java peer holds delegates into this tracker; release it once the provider no longer uses it.
			stopped.Dispose();
		}
	}

	/// <summary>
	/// Tells whether no stop, failure or newer start replaced the given start.
	/// </summary>
	/// <param name="attempt">The start to check.</param>
	/// <returns><see langword="true"/> when the start is still the current one.</returns>
	private bool IsCurrent(int attempt)
	{
		lock (gate)
		{
			return running && attempt == generation;
		}
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that a start of the location updates was requested.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Location updates start requested")]
	private static partial void LogStartRequested(ILogger logger);

	/// <summary>
	/// Logs that the provider accepted the request for location updates.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Location updates started")]
	private static partial void LogStarted(ILogger logger);

	/// <summary>
	/// Logs that the location updates failed or were refused, with the message the screen shows and the cause when there is one.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="message">What went wrong.</param>
	/// <param name="exception">The cause, or <see langword="null"/>.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Location updates failed: {Message}")]
	private static partial void LogFailed(ILogger logger, string message, Exception? exception);

	/// <summary>
	/// Logs that removing the updates from the provider failed.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">The cause.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Location updates could not be removed")]
	private static partial void LogRemoveFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs that the provider has no position for now while the updates go on.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Location unavailable for now, still listening")]
	private static partial void LogInterrupted(ILogger logger);

	/// <summary>
	/// Logs the first fix after a start, with its age by the clock of the device: a large age explains why the screen drops it as stale.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="accuracyMetres">The accuracy of the fix in metres.</param>
	/// <param name="ageSeconds">Seconds between the time stamp of the fix and now.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "First location fix received: accuracy {AccuracyMetres} m, age {AgeSeconds} s")]
	private static partial void LogFirstFix(ILogger logger, double accuracyMetres, double ageSeconds);
	#endregion
}
