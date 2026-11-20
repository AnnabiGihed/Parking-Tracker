using Microsoft.AspNetCore.Components;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Features.Shared.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The shell every dialog shares: a dimmed backdrop with a sheet that rises from the bottom of the phone (centred on
///              a wide screen). It draws its content only while the view model says it is open, so that closing a dialog is a
///              state change and needs no script. A dialog that passes <see cref="OnBack"/> is also closed by the back button of the
///              phone while it is open (ADR-0030).
/// </summary>
public sealed partial class DialogShell : ComponentBase, IDisposable
{
	#region Injected Services
	/// <summary>
	/// Gets or sets the stack of things the back button closes.
	/// </summary>
	[Inject]
	public IBackNavigation BackNavigation { get; set; } = null!;
	#endregion

	#region Parameters
	/// <summary>
	/// Gets or sets a value indicating whether the dialog is open.
	/// </summary>
	[Parameter]
	public bool IsOpen { get; set; }

	/// <summary>
	/// Gets or sets the accessible name of the dialog.
	/// </summary>
	[Parameter]
	public string Label { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the content of the dialog.
	/// </summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	/// <summary>
	/// Gets or sets what happens when the back button is pressed while the dialog is open; the dialog does not react to it when unset.
	/// </summary>
	[Parameter]
	public EventCallback OnBack { get; set; }
	#endregion

	#region Fields
	/// <summary>
	/// Makes the back button close the dialog while it is open; <see langword="null"/> while it is closed.
	/// </summary>
	private IDisposable? backRegistration;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override void OnParametersSet()
	{
		if (IsOpen && OnBack.HasDelegate)
		{
			backRegistration ??= BackNavigation.Register(() => _ = InvokeAsync(OnBack.InvokeAsync));
		}
		else
		{
			ReleaseBack();
		}
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose() => ReleaseBack();
	#endregion

	#region Private Helpers
	/// <summary>
	/// Stops the back button from closing the dialog.
	/// </summary>
	private void ReleaseBack()
	{
		backRegistration?.Dispose();
		backRegistration = null;
	}
	#endregion
}
