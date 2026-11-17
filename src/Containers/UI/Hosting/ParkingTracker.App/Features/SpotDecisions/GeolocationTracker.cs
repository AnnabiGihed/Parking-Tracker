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
public sealed class GeolocationTracker : ILocationTracker
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
	/// Guards <see cref="running"/>.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// Whether the tracker was started and has not stopped.
	/// </summary>
	private bool running;
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
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		lock (gate)
		{
			if (running)
			{
				return;
			}

			running = true;
		}

		Geolocation.Default.LocationChanged += OnLocationChanged;
		Geolocation.Default.ListeningFailed += OnListeningFailed;
		try
		{
			var request = new GeolocationListeningRequest(GeolocationAccuracy.Best, Interval);

			// MAUI asks for the permission here when it is missing, which needs the main thread.
			var started = await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.StartListeningForegroundAsync(request)).ConfigureAwait(false);
			if (!started)
			{
				Fail("location could not be started");
			}
		}
		catch (PermissionException)
		{
			Fail("location not allowed");
		}
		catch (FeatureNotEnabledException)
		{
			Fail("location is turned off");
		}
		catch (FeatureNotSupportedException)
		{
			Fail("location is not available on this device");
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
		}

		Geolocation.Default.LocationChanged -= OnLocationChanged;
		Geolocation.Default.ListeningFailed -= OnListeningFailed;
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
	/// Stops the tracker and tells the listeners why.
	/// </summary>
	/// <param name="message">What went wrong, in words for the user.</param>
	private void Fail(string message)
	{
		lock (gate)
		{
			running = false;
		}

		Geolocation.Default.LocationChanged -= OnLocationChanged;
		Geolocation.Default.ListeningFailed -= OnListeningFailed;
		ErrorOccurred?.Invoke(this, new LocationErrorEventArgs(message));
	}
	#endregion
}
