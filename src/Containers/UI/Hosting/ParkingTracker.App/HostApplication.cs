using ParkingTracker.App.Features.Shared;
using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.App;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The MAUI application: opens the one window with the page that hosts the Blazor screens and tells the rest of the app
///              when the user comes back to it, so that the location permission is read again after a visit to the settings, and
///              whether the window is on screen, so that a parking alert is shown as a message instead of a notification. It also starts the
///              start-up gate: the one-time copy of the saved data of the previous version (ADR-0008) and the
///              device database (ADR-0026).
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

	/// <summary>
	/// Prepares the saved data: the previous version's copy and the device database.
	/// </summary>
	private readonly StartupGate gate;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="HostApplication"/> class.
	/// </summary>
	/// <param name="resumeNotifier">Passes the "app resumed" moment on to the screens.</param>
	/// <param name="visibility">Records whether the window is on screen.</param>
	/// <param name="gate">Prepares the saved data: the previous version's copy and the device database.</param>
	public HostApplication(AppResumeNotifier resumeNotifier, AppVisibility visibility, StartupGate gate)
	{
		this.resumeNotifier = resumeNotifier;
		this.visibility = visibility;
		this.gate = gate;
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Starts on a pool thread before the web view exists; the screens and the relay wait for the same run before they read settings.
		_ = gate.RunOnceAsync();
		var window = new Window(new MainPage());
		window.Activated += (_, _) => visibility.MarkVisible();
		window.Deactivated += (_, _) => visibility.MarkHidden();
		window.Stopped += (_, _) => visibility.MarkHidden();
		window.Resumed += (_, _) => resumeNotifier.NotifyResumed();
		return window;
	}
	#endregion
}
