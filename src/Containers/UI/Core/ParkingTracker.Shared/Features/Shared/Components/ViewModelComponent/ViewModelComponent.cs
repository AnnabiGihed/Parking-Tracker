using Microsoft.AspNetCore.Components;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Features.Shared.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Base of the components that render one view model: injects it, renders again whenever its <c>Changed</c> event
///              occurs (also from a timer thread, through the component's own dispatcher) and stops listening when the component goes away.
/// </summary>
/// <typeparam name="TViewModel">The view model the component renders.</typeparam>
public abstract class ViewModelComponent<TViewModel> : ComponentBase, IDisposable
	where TViewModel : ViewModelBase
{
	#region Fields
	/// <summary>
	/// Whether the component was disposed, so that a late event does not render it.
	/// </summary>
	private bool disposed;
	#endregion

	#region Injected Services
	/// <summary>
	/// Gets or sets the view model the component renders.
	/// </summary>
	[Inject]
	public TViewModel ViewModel { get; set; } = null!;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override void OnInitialized()
	{
		ViewModel.Changed += OnViewModelChanged;
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
	#endregion

	#region Protected Methods
	/// <summary>
	/// Stops listening to the view model.
	/// </summary>
	/// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			disposed = true;
			ViewModel.Changed -= OnViewModelChanged;
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Renders the component again after the view model changed.
	/// </summary>
	/// <param name="sender">The view model.</param>
	/// <param name="e">Not used.</param>
	private void OnViewModelChanged(object? sender, EventArgs e)
	{
		if (!disposed)
		{
			_ = InvokeAsync(StateHasChanged);
		}
	}
	#endregion
}
