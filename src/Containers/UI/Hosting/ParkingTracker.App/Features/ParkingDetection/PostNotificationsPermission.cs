using Microsoft.Maui.ApplicationModel;

namespace ParkingTracker.App.Features.ParkingDetection;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The runtime permission to show notifications (<c>POST_NOTIFICATIONS</c>, Android 13 and later) as a MAUI permission,
///              so that it is asked and read through the same <c>Permissions</c> calls as the location. MAUI has no ready-made type
///              for it that this project relies on; <see cref="Permissions.BasePlatformPermission"/> is the documented way to add one.
/// </summary>
internal sealed class PostNotificationsPermission : Permissions.BasePlatformPermission
{
	#region Properties
	/// <inheritdoc />
	public override (string androidPermission, bool isRuntime)[] RequiredPermissions => [(global::Android.Manifest.Permission.PostNotifications, true)];
	#endregion
}
