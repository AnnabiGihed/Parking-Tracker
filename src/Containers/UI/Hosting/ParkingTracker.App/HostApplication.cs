using ParkingTracker.App.Features.Shared;

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The MAUI application: opens the one window with the page that hosts the Blazor screens and tells the rest of the app
///              when the user comes back to it, so that the location permission is read again after a visit to the settings, and
///              whether the window is on screen, so that a parking alert is shown as a message instead of a notification.
/// </summary>
public sealed class HostApplication : Microsoft.Maui.Controls.Application
{
	#region Fields
	/// <summary>
	/// Passes the "app resumed" moment on to the screens.
	/// </summary>
	private readonly AppResumeNotifier resumeNotifier;

	/// <summary>
	/// Records whether the window is on screen.
	/// </summary>
	private readonly AppVisibility visibility;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="HostApplication"/> class.
	/// </summary>
	/// <param name="resumeNotifier">Passes the "app resumed" moment on to the screens.</param>
	/// <param name="visibility">Records whether the window is on screen.</param>
	public HostApplication(AppResumeNotifier resumeNotifier, AppVisibility visibility)
	{
		this.resumeNotifier = resumeNotifier;
		this.visibility = visibility;
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage());
		window.Activated += (_, _) => visibility.MarkVisible();
		window.Deactivated += (_, _) => visibility.MarkHidden();
		window.Stopped += (_, _) => visibility.MarkHidden();
		window.Resumed += (_, _) => resumeNotifier.NotifyResumed();
		return window;
	}
	#endregion
}
