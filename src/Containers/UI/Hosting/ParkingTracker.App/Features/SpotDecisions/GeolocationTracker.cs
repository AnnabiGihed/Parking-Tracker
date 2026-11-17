using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.App.Features.SpotDecisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ILocationTracker"/> of the phone over MAUI <c>Geolocation</c> continuous listening in the foreground:
///              the best accuracy, a fix about every second and no minimum distance, so that fixes keep coming while the phone stands
///              still (the screen reports a lost signal after 15 seconds without one). Events are raised on the thread of the platform.
///              Background tracking with the app off screen is a later stage (ADR-0002). When the request is refused or listening
///              fails, the tracker stops and says why through <see cref="ErrorOccurred"/>.
/// </summary>
public sealed partial class GeolocationTracker : ILocationTracker
{
	#region Constants
	/// <summary>
	/// The accuracy given to a fix that reports none, large enough that the decision treats it as unreliable.
	/// </summary>
	private const double UnknownAccuracyMetres = 9999;

	/// <summary>
	/// The time between fixes asked of the device.
	/// </summary>
	private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
	#endregion

	#region Fields
	/// <summary>
	/// The log, where the start, a failure and the first fix of a start go, so that a run without a position can be explained.
	/// </summary>
	private readonly ILogger<GeolocationTracker> logger;

	/// <summary>
	/// Guards <see cref="running"/> and <see cref="generation"/>.
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
	/// Whether the first fix of the current start was logged; 0 or 1, changed with <see cref="Interlocked"/> because fixes arrive on any thread.
	/// </summary>
	private int firstFixLogged;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GeolocationTracker"/> class.
	/// </summary>
	/// <param name="logger">The log, where the start, a failure and the first fix of a start go.</param>
	public GeolocationTracker(ILogger<GeolocationTracker> logger)
	{
		ArgumentNullException.ThrowIfNull(logger);
		this.logger = logger;
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
	/// A refusal or any other failure unsubscribes, resets <see cref="IsRunning"/> and is reported through <see cref="ErrorOccurred"/>.
	/// A cancelled start also cleans up, and then throws the <see cref="OperationCanceledException"/>.
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

		Geolocation.Default.LocationChanged += OnLocationChanged;
		Geolocation.Default.ListeningFailed += OnListeningFailed;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			var request = new GeolocationListeningRequest(GeolocationAccuracy.Best, Interval);

			// MAUI asks for the permission here when it is missing, which needs the main thread.
			var started = await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.StartListeningForegroundAsync(request)).ConfigureAwait(false);
			if (!started)
			{
				Fail("location could not be started", attempt);
				return;
			}

			LogStarted(logger);

			// The platform is listening now: a cancel or a stop that came meanwhile must not leave it on without handlers.
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsCurrent(attempt))
			{
				await StopListeningWhenIdleAsync().ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			await AbortAsync(attempt).ConfigureAwait(false);
			throw;
		}
		catch (PermissionException)
		{
			Fail("location not allowed", attempt);
		}
		catch (FeatureNotEnabledException)
		{
			Fail("location is turned off", attempt);
		}
		catch (FeatureNotSupportedException)
		{
			Fail("location is not available on this device", attempt);
		}
		catch (Exception)
		{
			Fail("location could not be started", attempt);
		}
	}

	/// <inheritdoc />
	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		lock (gate)
		{
			if (!running)
			{
				return;
			}

			running = false;
			generation++;
		}

		Unsubscribe();
		await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.StopListeningForeground()).ConfigureAwait(false);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Passes a position of the device on as a fix.
	/// </summary>
	/// <param name="sender">The geolocation service.</param>
	/// <param name="e">The position.</param>
	private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
	{
		var location = e.Location;
		if (Interlocked.Exchange(ref firstFixLogged, 1) == 0)
		{
			LogFirstFix(logger, location.Accuracy ?? UnknownAccuracyMetres, Math.Round((TimeProvider.System.GetUtcNow() - location.Timestamp).TotalSeconds, 1));
		}

		var fix = new GpsFix(
			new GeoPoint(location.Latitude, location.Longitude),
			location.Accuracy ?? UnknownAccuracyMetres,
			location.Speed,
			location.Timestamp);
		FixReceived?.Invoke(this, new LocationFixEventArgs(fix));
	}

	/// <summary>
	/// Reports that the device stopped delivering positions.
	/// </summary>
	/// <param name="sender">The geolocation service.</param>
	/// <param name="e">What went wrong.</param>
	private void OnListeningFailed(object? sender, GeolocationListeningFailedEventArgs e)
	{
		Fail(e.Error == GeolocationError.Unauthorized ? "location not allowed" : "no position available");
	}

	/// <summary>
	/// Stops the tracker and tells the listeners why. Nothing happens when the tracker already stopped or when the start that
	/// failed was replaced by a newer one.
	/// </summary>
	/// <param name="message">What went wrong, in words for the user.</param>
	/// <param name="attempt">The start that failed, or <see langword="null"/> for a failure reported by the platform.</param>
	private void Fail(string message, int? attempt = null)
	{
		lock (gate)
		{
			if (!running || (attempt is { } started && started != generation))
			{
				return;
			}

			running = false;
			generation++;
		}

		LogFailed(logger, message);
		Unsubscribe();
		ErrorOccurred?.Invoke(this, new LocationErrorEventArgs(message));
	}

	/// <summary>
	/// Undoes a start that was cancelled: unsubscribes, resets the flag and stops the platform when nothing else uses it.
	/// </summary>
	/// <param name="attempt">The start that was cancelled.</param>
	/// <returns>A task that completes when the platform no longer listens for this start.</returns>
	private async Task AbortAsync(int attempt)
	{
		bool owner;
		lock (gate)
		{
			owner = running && attempt == generation;
			if (owner)
			{
				running = false;
				generation++;
			}
		}

		if (owner)
		{
			Unsubscribe();
		}

		await StopListeningWhenIdleAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Stops the platform, unless a newer start is running that needs it.
	/// </summary>
	/// <returns>A task that completes when the platform was told to stop.</returns>
	private Task StopListeningWhenIdleAsync()
	{
		lock (gate)
		{
			if (running)
			{
				return Task.CompletedTask;
			}
		}

		return MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.StopListeningForeground());
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

	/// <summary>
	/// Removes the handlers of the platform events.
	/// </summary>
	private void Unsubscribe()
	{
		Geolocation.Default.LocationChanged -= OnLocationChanged;
		Geolocation.Default.ListeningFailed -= OnListeningFailed;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that a start of the location listening was requested.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Location listening start requested")]
	private static partial void LogStartRequested(ILogger logger);

	/// <summary>
	/// Logs that the platform accepted the start of the location listening.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Location listening started")]
	private static partial void LogStarted(ILogger logger);

	/// <summary>
	/// Logs that the location listening failed or was refused, with the message the screen shows.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="message">What went wrong.</param>
	[LoggerMessage(Level = LogLevel.Information, Message = "Location listening failed: {Message}")]
	private static partial void LogFailed(ILogger logger, string message);

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
