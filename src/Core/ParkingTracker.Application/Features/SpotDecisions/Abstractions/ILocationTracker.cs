namespace ParkingTracker.Application.Features.SpotDecisions.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the position source of the device. It delivers high-accuracy fixes as events while it runs; a failure
///              (no permission, no signal source) is an event too, because it can happen at any time after the start.
///              Both events can be raised on any thread, so a receiver must not assume the UI thread or take a lock that a
///              raiser could hold. An implementation must also deliver periodic fixes while the device stands still (a fused
///              location request at a time interval, not only after a minimum distance), because the screen reports
///              "Lost the GPS signal" after 15 seconds without a fix.
/// </summary>
public interface ILocationTracker
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether the tracker is delivering fixes.
	/// </summary>
	bool IsRunning { get; }
	#endregion

	#region Methods
	/// <summary>
	/// Starts delivering high-accuracy fixes. Starting a running tracker does nothing.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the device accepted the request; a refusal is reported through <see cref="ErrorOccurred"/>.</returns>
	Task StartAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stops delivering fixes. Stopping a stopped tracker does nothing.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the tracker stopped.</returns>
	Task StopAsync(CancellationToken cancellationToken = default);
	#endregion

	#region Events
	/// <summary>
	/// Occurs when the device reports a position, possibly on any thread. The fix can be older than a few seconds; the receiver judges its age.
	/// It must keep occurring about every few seconds while the device is stationary.
	/// </summary>
	event EventHandler<LocationFixEventArgs>? FixReceived;

	/// <summary>
	/// Occurs when the position cannot be read, possibly on any thread. The tracker has stopped when this occurs.
	/// </summary>
	event EventHandler<LocationErrorEventArgs>? ErrorOccurred;
	#endregion
}
