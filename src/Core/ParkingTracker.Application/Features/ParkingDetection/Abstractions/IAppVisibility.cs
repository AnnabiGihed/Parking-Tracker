namespace ParkingTracker.Application.Features.ParkingDetection.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that tells whether the app is on screen at this moment. The parking alert uses it as the first version did: with
///              the app in front, the user is told by a message on the screen and the place is checked again, not by a notification.
/// </summary>
public interface IAppVisibility
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether the app is in front of the user: its window is active. It is <see langword="false"/> while
	/// the app is in the background, closed, or only kept alive by the background service.
	/// </summary>
	bool IsVisible { get; }
	#endregion
}
