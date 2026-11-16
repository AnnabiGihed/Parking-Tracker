using ParkingTracker.Application.Sources;

namespace ParkingTracker.Infrastructure.Tests.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A network status with a fixed value, for tests.
/// </summary>
public sealed class StaticNetworkStatus : INetworkStatus
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="StaticNetworkStatus"/> class.
	/// </summary>
	/// <param name="isOnline">Whether the device is online.</param>
	public StaticNetworkStatus(bool isOnline)
	{
		IsOnline = isOnline;
	}
	#endregion

	#region INetworkStatus Implementation
	/// <inheritdoc />
	public bool IsOnline { get; }
	#endregion
}
