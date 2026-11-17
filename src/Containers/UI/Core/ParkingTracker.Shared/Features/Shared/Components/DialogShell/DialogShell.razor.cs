using Microsoft.AspNetCore.Components;

namespace ParkingTracker.Shared.Features.Shared.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The shell every dialog shares: a dimmed backdrop with a sheet that rises from the bottom of the phone (centred on
///              a wide screen). It draws its content only while the view model says it is open, so that closing a dialog is a
///              state change and needs no script.
/// </summary>
public sealed partial class DialogShell : ComponentBase
{
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
	#endregion
}
