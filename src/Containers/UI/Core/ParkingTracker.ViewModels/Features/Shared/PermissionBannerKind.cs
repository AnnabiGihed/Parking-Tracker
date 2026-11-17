namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Which banner the bottom sheet shows about the location permission.
/// </summary>
public enum PermissionBannerKind
{
	/// <summary>
	/// No banner: location is allowed enough, or the user postponed the question.
	/// </summary>
	None,

	/// <summary>
	/// Location is off: the banner offers to turn it on.
	/// </summary>
	TurnOn,

	/// <summary>
	/// Location is allowed while the app is in use: the banner offers "all the time".
	/// </summary>
	AllowAlways,
}
