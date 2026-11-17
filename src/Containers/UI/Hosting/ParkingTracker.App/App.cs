using ParkingTracker.App.Features.Shared;

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The MAUI application: opens the one window with the page that hosts the Blazor screens and tells the rest of the app
///              when the user comes back to it, so that the location permission is read again after a visit to the settings.
/// </summary>
public sealed class App : Microsoft.Maui.Controls.Application
{
	#region Fields
	/// <summary>
	/// Passes the "app resumed" moment on to the screens.
	/// </summary>
	private readonly AppResumeNotifier resumeNotifier;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="App"/> class.
	/// </summary>
	/// <param name="resumeNotifier">Passes the "app resumed" moment on to the screens.</param>
	public App(AppResumeNotifier resumeNotifier)
	{
		this.resumeNotifier = resumeNotifier;
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage());
		window.Resumed += (_, _) => resumeNotifier.NotifyResumed();
		return window;
	}
	#endregion
}
