namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Carries the "app resumed" moment from the MAUI window, which lives outside the Blazor scope, to the
///              <see cref="PlatformRelay"/>, which lives inside it and can reach the view models.
/// </summary>
public sealed class AppResumeNotifier
{
	#region Events
	/// <summary>
	/// Occurs when the user comes back to the app, for example from the location settings.
	/// </summary>
	public event EventHandler? Resumed;
	#endregion

	#region Public Methods
	/// <summary>
	/// Tells the listeners that the app was resumed.
	/// </summary>
	public void NotifyResumed()
	{
		Resumed?.Invoke(this, EventArgs.Empty);
	}
	#endregion
}
