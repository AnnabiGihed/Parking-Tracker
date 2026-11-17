using Microsoft.Maui.ApplicationModel;
using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ILocationPermissions"/> of the phone over MAUI <c>Permissions</c>. The foreground permission is asked in
///              the system dialog; "Allow all the time" can only be chosen by the user in the location settings of the app, which
///              <see cref="OpenSettingsAsync"/> opens, so the background permission is only read, never requested.
/// </summary>
public sealed class MauiLocationPermissions : ILocationPermissions
{
	#region ILocationPermissions Implementation
	/// <inheritdoc />
	public async Task<LocationPermissionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
	{
		var foreground = await MainThread.InvokeOnMainThreadAsync(() => Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>()).ConfigureAwait(false);
		var background = await MainThread.InvokeOnMainThreadAsync(() => Permissions.CheckStatusAsync<Permissions.LocationAlways>()).ConfigureAwait(false);
		return new LocationPermissionStatus(ToState(foreground), ToState(background));
	}

	/// <inheritdoc />
	public async Task<bool> RequestForegroundAsync(CancellationToken cancellationToken = default)
	{
		// The system dialog can only be shown from the main thread.
		var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.LocationWhenInUse>()).ConfigureAwait(false);
		return status == PermissionStatus.Granted;
	}

	/// <inheritdoc />
	public Task OpenSettingsAsync(CancellationToken cancellationToken = default)
	{
		return MainThread.InvokeOnMainThreadAsync(() => AppInfo.Current.ShowSettingsUI());
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Translates the answer of MAUI to the port's state: anything that is not granted or unknown counts as denied, including a
	/// permission that the device restricts.
	/// </summary>
	/// <param name="status">The answer of MAUI.</param>
	/// <returns>The state for the view models.</returns>
	private static PermissionState ToState(PermissionStatus status)
	{
		return status switch
		{
			PermissionStatus.Granted => PermissionState.Granted,
			PermissionStatus.Unknown => PermissionState.Unknown,
			_ => PermissionState.Denied,
		};
	}
	#endregion
}
