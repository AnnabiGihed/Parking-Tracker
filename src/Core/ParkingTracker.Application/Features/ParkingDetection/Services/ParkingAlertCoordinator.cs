using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.Application.Features.ParkingDetection.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="IParkingAlertCoordinator"/> over the saved switch, the location permissions and the background service.
/// </summary>
public sealed class ParkingAlertCoordinator : IParkingAlertCoordinator
{
	#region Fields
	/// <summary>
	/// The saved settings, which hold the alerts switch.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// The location permissions of the device.
	/// </summary>
	private readonly ILocationPermissions permissions;

	/// <summary>
	/// The background service of the device.
	/// </summary>
	private readonly IParkingWatchService service;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingAlertCoordinator"/> class.
	/// </summary>
	/// <param name="settings">The saved settings, which hold the alerts switch.</param>
	/// <param name="permissions">The location permissions of the device.</param>
	/// <param name="service">The background service of the device.</param>
	public ParkingAlertCoordinator(ISettingsStore settings, ILocationPermissions permissions, IParkingWatchService service)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(permissions);
		ArgumentNullException.ThrowIfNull(service);
		this.settings = settings;
		this.permissions = permissions;
		this.service = service;
	}
	#endregion

	#region IParkingAlertCoordinator Implementation
	/// <inheritdoc />
	public async Task<bool> ShouldRunAsync(CancellationToken cancellationToken = default)
	{
		if (!await settings.GetAlertsEnabledAsync().ConfigureAwait(false))
		{
			return false;
		}

		var status = await permissions.GetStatusAsync(cancellationToken).ConfigureAwait(false);
		return status.Foreground == PermissionState.Granted && status.Background == PermissionState.Granted;
	}

	/// <inheritdoc />
	public async Task<bool> ApplyAsync(CancellationToken cancellationToken = default)
	{
		if (await ShouldRunAsync(cancellationToken).ConfigureAwait(false))
		{
			await service.StartAsync(cancellationToken).ConfigureAwait(false);
			return true;
		}

		await service.StopAsync(cancellationToken).ConfigureAwait(false);
		return false;
	}
	#endregion
}
