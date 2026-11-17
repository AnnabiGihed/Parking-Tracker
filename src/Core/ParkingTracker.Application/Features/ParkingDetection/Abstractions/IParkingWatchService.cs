namespace ParkingTracker.Application.Features.ParkingDetection.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the background service of the phone that follows the position while the app is not on screen, so that
///              parking is noticed and announced. The phone shows a small permanent notification while it runs.
/// </summary>
public interface IParkingWatchService
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether the service was started and has not been stopped.
	/// </summary>
	bool IsRunning { get; }
	#endregion

	#region Methods
	/// <summary>
	/// Starts the service; starting a running service does nothing.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the phone accepted the request.</returns>
	Task StartAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stops the service; stopping a stopped service does nothing.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the service was asked to stop.</returns>
	Task StopAsync(CancellationToken cancellationToken = default);
	#endregion
}
