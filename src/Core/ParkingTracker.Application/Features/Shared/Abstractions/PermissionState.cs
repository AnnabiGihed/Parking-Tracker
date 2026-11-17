namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer of the device about one location permission.
/// </summary>
public enum PermissionState
{
	/// <summary>
	/// The device cannot tell, for example in a browser.
	/// </summary>
	Unknown,

	/// <summary>
	/// The user allowed it.
	/// </summary>
	Granted,

	/// <summary>
	/// The user has not allowed it.
	/// </summary>
	Denied,
}
