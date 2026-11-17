namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the location permissions of the device. Android asks for the position while the app is in use first and
///              only lets the user choose "Allow all the time" in the location settings of the app, so the second step is a
///              button that opens those settings.
/// </summary>
public interface ILocationPermissions
{
	#region Methods
	/// <summary>
	/// Reads what the user allowed.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The status of the foreground and background permissions.</returns>
	Task<LocationPermissionStatus> GetStatusAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Asks the user to allow the position while the app is in use.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns><see langword="true"/> when the user allowed it.</returns>
	Task<bool> RequestForegroundAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens the location settings of the app, where the user can choose "Allow all the time".
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the settings were opened.</returns>
	Task OpenSettingsAsync(CancellationToken cancellationToken = default);
	#endregion
}
