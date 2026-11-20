namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The stack of things the phone's back button closes, newest first: the side menu, a Radzen dialog, a custom dialog. A
///              screen registers a close action while it is open; the host asks <see cref="TryGoBack"/> when the back button is
///              pressed and leaves the app only when nothing is open.
/// </summary>
public interface IBackNavigation
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether something is open that the back button would close.
	/// </summary>
	bool CanGoBack { get; }
	#endregion

	#region Methods
	/// <summary>
	/// Registers what the back button closes while a screen is open.
	/// </summary>
	/// <param name="close">Closes the screen.</param>
	/// <returns>A registration to dispose when the screen closed by itself.</returns>
	IDisposable Register(Action close);

	/// <summary>
	/// Closes the screen that was opened last.
	/// </summary>
	/// <returns><see langword="true"/> when something was closed; <see langword="false"/> when nothing was open.</returns>
	bool TryGoBack();
	#endregion
}
