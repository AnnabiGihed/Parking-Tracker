using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A view model with no content that only opens and closes, so that the dialog bridge is tested without a real screen.
/// </summary>
internal sealed class FakeDialogViewModel : ViewModelBase, IDialogViewModel
{
	#region Properties
	/// <inheritdoc />
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Gets how many times <see cref="Close"/> was called.
	/// </summary>
	public int CloseCalls { get; private set; }
	#endregion

	#region Public Methods
	/// <summary>
	/// Opens the dialog.
	/// </summary>
	public void Open()
	{
		IsOpen = true;
		NotifyChanged();
	}

	/// <inheritdoc />
	public void Close()
	{
		CloseCalls++;
		IsOpen = false;
		NotifyChanged();
	}
	#endregion
}
