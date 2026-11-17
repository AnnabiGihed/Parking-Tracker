namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tells whether the background service of the parking alerts exists in this process. The service sets it when
///              the system creates and destroys it, and <see cref="AndroidParkingWatchService"/> reads it. It is one registered
///              instance, not a static field, so that the service does not write shared state from an instance method.
/// </summary>
public sealed class ParkingServiceState
{
	#region Fields
	/// <summary>
	/// Whether the service exists; written by the thread of the service, read from any thread.
	/// </summary>
	private volatile bool running;
	#endregion

	#region Properties
	/// <summary>
	/// Gets a value indicating whether the service was created and has not been destroyed.
	/// </summary>
	public bool IsRunning => running;
	#endregion

	#region Public Methods
	/// <summary>
	/// Records that the system created the service.
	/// </summary>
	public void MarkCreated()
	{
		running = true;
	}

	/// <summary>
	/// Records that the system destroyed the service.
	/// </summary>
	public void MarkDestroyed()
	{
		running = false;
	}
	#endregion
}
