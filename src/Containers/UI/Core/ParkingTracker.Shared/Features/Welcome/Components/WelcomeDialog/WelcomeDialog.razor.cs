using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.Welcome;

namespace ParkingTracker.Shared.Features.Welcome.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The welcome dialog of the first launch: what the app does, why it needs the location "all the time" with a button
///              that leads to the settings of the app, and "Add my parking card". It renders the <see cref="WelcomeViewModel"/>
///              and, for the permission sentence, the <see cref="LocationPermissionViewModel"/> it holds.
/// </summary>
public sealed partial class WelcomeDialog : ViewModelComponent<WelcomeViewModel>
{
	#region Lifecycle Methods
	/// <inheritdoc />
	protected override void OnInitialized()
	{
		base.OnInitialized();
		ViewModel.Permission.Changed += OnPermissionChanged;
	}
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles "Allow location all the time".
	/// </summary>
	/// <returns>A task that completes when the request was handled.</returns>
	private Task OnAllowAlwaysAsync() => ViewModel.AllowLocationAlwaysAsync();

	/// <summary>
	/// Handles "Add my parking card".
	/// </summary>
	/// <returns>A task that completes when the dialog was closed.</returns>
	private Task OnAddCardAsync() => ViewModel.AddCardAsync();
	#endregion

	#region Private Helpers
	/// <summary>
	/// Renders the dialog again after the permission state changed.
	/// </summary>
	/// <param name="sender">The permission view model.</param>
	/// <param name="e">Not used.</param>
	private void OnPermissionChanged(object? sender, EventArgs e)
	{
		_ = InvokeAsync(StateHasChanged);
	}
	#endregion
}
