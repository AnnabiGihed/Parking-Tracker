namespace ParkingTracker.Application.Features.ParkingDetection.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Keeps the background service in line with what the user chose: it runs while parking alerts are on and the
///              location is allowed all the time, and is stopped otherwise.
/// </summary>
public interface IParkingAlertCoordinator
{
	#region Methods
	/// <summary>
	/// Tells whether the background service should run now: alerts on, and the foreground and background location allowed.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with <see langword="true"/> when the service should run.</returns>
	Task<bool> ShouldRunAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Starts or stops the background service so that it matches <see cref="ShouldRunAsync"/>. Call it when the app opens, when
	/// the user returns from the settings and when the alerts switch changes.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with <see langword="true"/> when the service runs afterwards.</returns>
	Task<bool> ApplyAsync(CancellationToken cancellationToken = default);
	#endregion
}
