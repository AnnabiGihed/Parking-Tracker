using ParkingTracker.Application.Features.ParkingDetection.Abstractions;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="IAppVisibility"/> of the phone: the app is on screen from the moment its window is activated until it
///              is deactivated or stopped, as reported by <see cref="HostApplication"/>. It starts as not visible, because the
///              background service can run while no window exists; the window activation at launch makes it visible.
/// </summary>
public sealed class AppVisibility : IAppVisibility
{
	#region Fields
	/// <summary>
	/// Whether the window is active; written by the main thread, read by the thread of the background loop.
	/// </summary>
	private volatile bool visible;
	#endregion

	#region Properties
	/// <inheritdoc />
	public bool IsVisible => visible;
	#endregion

	#region Public Methods
	/// <summary>
	/// Records that the window became active or inactive.
	/// </summary>
	/// <param name="isVisible">Whether the app is in front of the user now.</param>
	public void Set(bool isVisible)
	{
		visible = isVisible;
	}
	#endregion
}
