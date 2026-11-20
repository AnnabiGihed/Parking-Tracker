using Microsoft.AspNetCore.Components;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Menu;
using ParkingTracker.ViewModels.Features.Shared;
using Radzen;

namespace ParkingTracker.Shared.Features.Menu.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The content of the side menu (ADR-0030): the app header and a Radzen panel menu with Home, Work and the parking card,
///              each with what is saved under it. A tap closes the menu and then opens the screen of the entry, so that a Radzen
///              dialog is never open over one of the app's own dialogs. While it is open, the back button of the phone closes it.
/// </summary>
public sealed partial class AppMenu : ViewModelComponent<MenuViewModel>
{
	#region Injected Services
	/// <summary>
	/// Gets or sets the service that closes the side menu.
	/// </summary>
	[Inject]
	public DialogService DialogService { get; set; } = null!;

	/// <summary>
	/// Gets or sets the stack of things the back button closes.
	/// </summary>
	[Inject]
	public IBackNavigation BackNavigation { get; set; } = null!;
	#endregion

	#region Fields
	/// <summary>
	/// Makes the back button close the menu while it is open.
	/// </summary>
	private IDisposable? backRegistration;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override void OnInitialized()
	{
		base.OnInitialized();
		backRegistration = BackNavigation.Register(() => DialogService.CloseSide());
	}
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles a tap on Home.
	/// </summary>
	/// <returns>A task that completes when the place dialog opened.</returns>
	private Task OnHomeAsync() => CloseThenAsync(() => OpenPlace(PlaceKind.Home));

	/// <summary>
	/// Handles a tap on Work.
	/// </summary>
	/// <returns>A task that completes when the place dialog opened.</returns>
	private Task OnWorkAsync() => CloseThenAsync(() => OpenPlace(PlaceKind.Work));

	/// <summary>
	/// Handles a tap on the parking card.
	/// </summary>
	/// <returns>A task that completes when the card dialog opened.</returns>
	private Task OnCardAsync() => CloseThenAsync(ViewModel.OpenCardAsync);
	#endregion

	#region Protected Methods
	/// <inheritdoc />
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			backRegistration?.Dispose();
		}

		base.Dispose(disposing);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Closes the menu and then opens a screen. The menu is not awaited while it slides out (<c>CloseSideAsync</c> waits for the end of
	/// the animation): it is already out of the way of the screen, which opens above the mask of its own.
	/// </summary>
	/// <param name="openScreen">Opens the screen of the entry.</param>
	/// <returns>A task that completes when the screen opened.</returns>
	private Task CloseThenAsync(Func<Task> openScreen)
	{
		DialogService.CloseSide();
		return openScreen();
	}

	/// <summary>
	/// Opens the place dialog of a kind of place.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <returns>A task that is already complete, because the dialog opens at once.</returns>
	private Task OpenPlace(PlaceKind kind)
	{
		ViewModel.OpenPlace(kind);
		return Task.CompletedTask;
	}
	#endregion
}
