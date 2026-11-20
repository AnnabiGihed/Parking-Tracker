using Microsoft.AspNetCore.Components;
using ParkingTracker.ViewModels.Features.Shared;
using Radzen;

namespace ParkingTracker.Shared.Features.Shared.Dialogs;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Lets a view model drive a Radzen dialog without referencing Radzen (ADR-0020, ADR-0030): it opens the dialog, styled as a bottom sheet, through
///              <see cref="DialogService"/> when the view model says it is open and closes it when it says it is not, and when the
///              user closes the dialog (the close button, the mask, the escape key) it calls the view model's <c>Close</c>. While the
///              dialog is open the back button of the phone closes it. It draws nothing itself; its content is shown in the dialog.
/// </summary>
public sealed class DialogBridge : ComponentBase, IDisposable
{
	#region Injected Services
	/// <summary>
	/// Gets or sets the Radzen service that shows the dialog.
	/// </summary>
	[Inject]
	public DialogService DialogService { get; set; } = null!;

	/// <summary>
	/// Gets or sets the stack of things the back button closes.
	/// </summary>
	[Inject]
	public IBackNavigation BackNavigation { get; set; } = null!;
	#endregion

	#region Parameters
	/// <summary>
	/// Gets or sets the view model the dialog shows.
	/// </summary>
	[Parameter]
	[EditorRequired]
	public IDialogViewModel ViewModel { get; set; } = null!;

	/// <summary>
	/// Gets or sets the title of the dialog.
	/// </summary>
	[Parameter]
	public string Title { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the width of the dialog, as a CSS length; the sheet style limits it to 480px and centres it.
	/// </summary>
	[Parameter]
	public string Width { get; set; } = "100%";

	/// <summary>
	/// Gets or sets the content of the dialog.
	/// </summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }
	#endregion

	#region Constants
	/// <summary>
	/// The class that styles the dialog as a bottom sheet, like the dialogs of the app itself (see <c>.app-sheet</c> in style.css).
	/// </summary>
	public const string SheetClass = "app-sheet";
	#endregion

	#region Fields
	/// <summary>
	/// Whether this bridge has a dialog open in the service.
	/// </summary>
	private bool shown;

	/// <summary>
	/// Makes the back button close the dialog while it is open.
	/// </summary>
	private IDisposable? backRegistration;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override void OnInitialized()
	{
		ViewModel.Changed += OnViewModelChanged;
		DialogService.OnClose += OnDialogClosed;
		Synchronise();
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		ViewModel.Changed -= OnViewModelChanged;
		DialogService.OnClose -= OnDialogClosed;
		if (shown)
		{
			Hide();
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Brings the dialog in line with the view model, on the renderer's context because the view model may change on a timer thread.
	/// </summary>
	/// <param name="sender">The view model.</param>
	/// <param name="e">Not used.</param>
	private void OnViewModelChanged(object? sender, EventArgs e)
	{
		_ = InvokeAsync(Synchronise);
	}

	/// <summary>
	/// Opens the dialog when the view model opened, closes it when the view model closed, and draws it again otherwise.
	/// </summary>
	private void Synchronise()
	{
		if (ViewModel.IsOpen && !shown)
		{
			Show();
		}
		else if (!ViewModel.IsOpen && shown)
		{
			Hide();
		}
		else if (shown)
		{
			DialogService.Refresh();
		}
	}

	/// <summary>
	/// Opens the dialog and lets the back button close it.
	/// </summary>
	private void Show()
	{
		shown = true;
		backRegistration = BackNavigation.Register(ViewModel.Close);

		// The task ends when the dialog closes, which this bridge learns from OnClose, so it is not awaited.
		_ = DialogService.OpenAsync(Title, _ => ChildContent ?? (_ => { }), new DialogOptions { Width = Width, CssClass = SheetClass });
	}

	/// <summary>
	/// Closes the dialog the view model closed.
	/// </summary>
	private void Hide()
	{
		shown = false;
		backRegistration?.Dispose();
		backRegistration = null;
		DialogService.Close();
	}

	/// <summary>
	/// Tells the view model that the user closed the dialog.
	/// </summary>
	/// <param name="result">The result the dialog was closed with; not used.</param>
	private void OnDialogClosed(object? result)
	{
		if (!shown)
		{
			return;
		}

		shown = false;
		backRegistration?.Dispose();
		backRegistration = null;
		ViewModel.Close();
	}
	#endregion
}
