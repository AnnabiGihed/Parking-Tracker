namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What a view model shows to be driven by a dialog without knowing which dialog component draws it: whether it is open,
///              how to close it and when something changed. The <c>DialogBridge</c> of the Shared project joins it to Radzen's
///              <c>DialogService</c>, so the view model never references Radzen (ADR-0020, ADR-0030).
/// </summary>
public interface IDialogViewModel
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether the dialog is open.
	/// </summary>
	bool IsOpen { get; }
	#endregion

	#region Methods
	/// <summary>
	/// Closes the dialog without a result.
	/// </summary>
	void Close();
	#endregion

	#region Events
	/// <summary>
	/// Occurs when something the dialog shows has changed, including <see cref="IsOpen"/>.
	/// </summary>
	event EventHandler? Changed;
	#endregion
}
