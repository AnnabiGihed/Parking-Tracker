namespace ParkingTracker.Application.Features.ParkingDetection.Commands.HandleParked;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Says how the user was told about a parked car, so that the caller can finish the job when the notification was not
///              the way.
/// </summary>
public enum ParkedDelivery
{
	/// <summary>
	/// A notification was sent; the app was not in front of the user.
	/// </summary>
	Notified = 0,

	/// <summary>
	/// No notification was sent, because the app is on screen; the screen shows a message and checks the place itself.
	/// </summary>
	OnScreen = 1,
}
