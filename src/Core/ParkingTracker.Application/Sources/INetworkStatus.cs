namespace ParkingTracker.Application.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that tells whether the device has an internet connection.
/// </summary>
public interface INetworkStatus
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether the device has an internet connection.
	/// </summary>
	bool IsOnline { get; }
	#endregion
}
