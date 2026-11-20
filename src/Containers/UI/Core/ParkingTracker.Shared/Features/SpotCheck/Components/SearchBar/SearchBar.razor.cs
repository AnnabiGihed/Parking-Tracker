using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ParkingTracker.Shared.Features.Menu.Components;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.SpotCheck;
using Radzen;

namespace ParkingTracker.Shared.Features.SpotCheck.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The search bar at the top of the screen: the menu button that opens the side menu, a text box that
///              looks an address up through the <see cref="SearchViewModel"/> on submit, and the card button at its end. When
///              the search finds several places, the list of matches opens under the bar.
/// </summary>
public sealed partial class SearchBar : ViewModelComponent<SearchViewModel>
{
	#region Constants
	/// <summary>
	/// The title of the side menu, which names it for a screen reader.
	/// </summary>
	private const string MenuTitle = "Parking Tracker";

	/// <summary>
	/// The width of the side menu: wide enough for an address, narrow enough to leave the map visible behind the mask.
	/// </summary>
	private const string MenuWidth = "300px";

	/// <summary>
	/// The browser function that takes the focus off the focused element, which closes the keyboard of the phone. Blazor cannot do
	/// it by itself.
	/// </summary>
	private const string BlurFunction = "document.activeElement.blur";
	#endregion

	#region Injected Services
	/// <summary>
	/// Gets or sets the service that opens the side menu.
	/// </summary>
	[Inject]
	public DialogService DialogService { get; set; } = null!;

	/// <summary>
	/// Gets or sets the JavaScript runtime that closes the keyboard.
	/// </summary>
	[Inject]
	public IJSRuntime JS { get; set; } = null!;
	#endregion

	#region Private Helpers
	/// <summary>
	/// Takes the focus off the text box. A failure is ignored: the keyboard staying open is cosmetic, and the search must still run
	/// when the browser function is missing or the connection to the page was lost.
	/// </summary>
	/// <returns>A task that completes when the call ended.</returns>
	private async Task CloseKeyboardAsync()
	{
		try
		{
			await JS.InvokeVoidAsync(BlurFunction);
		}
		catch (JSException)
		{
			// Nothing to do: see the summary.
		}
		catch (JSDisconnectedException)
		{
			// Nothing to do: see the summary.
		}
	}
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles the submit of the search form: closes the keyboard, so that it does not cover the map and the list of matches, and
	/// then looks the text up.
	/// </summary>
	/// <returns>A task that completes when the result was shown.</returns>
	private async Task OnSubmitAsync()
	{
		await CloseKeyboardAsync();
		await ViewModel.SubmitAsync();
	}

	/// <summary>
	/// Handles the request to close the list of matches, which comes from the back button.
	/// </summary>
	private void OnCloseList() => ViewModel.CloseList();

	/// <summary>
	/// Handles a tap on the menu button: opens the side menu on the left, over a mask that closes it when tapped. The menu draws its
	/// own header, so the title bar and the close glyph of Radzen, which needs an icon font the app does not ship, stay off.
	/// </summary>
	private void OnOpenMenu()
	{
		DialogService.OpenSide<AppMenu>(MenuTitle, options: new SideDialogOptions
		{
			Position = DialogPosition.Left,
			ShowMask = true,
			CloseDialogOnOverlayClick = true,
			ShowTitle = false,
			ShowClose = false,
			Width = MenuWidth,
			AutoFocusFirstElement = false,
			CssClass = "app-menu-dialog",
			AriaLabel = MenuTitle,
		});
	}
	#endregion
}
