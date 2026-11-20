using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.Shared.Features.Places.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The top bar while the map picker is open (ADR-0030): a back arrow, the title ("Set home" or "Set work"), the
///              "Use my position" button and a search box. When a search finds several places, the list of matches opens under the
///              box. The back button of the phone first closes the list (which registers itself later than this header) and then
///              cancels the picker.
/// </summary>
public sealed partial class PlacePickerHeader : ViewModelComponent<PlacePickerViewModel>
{
	#region Fields
	/// <summary>
	/// Makes the back button cancel the picker while the header is shown.
	/// </summary>
	private IDisposable? backRegistration;
	#endregion

	#region Injected Services
	/// <summary>
	/// Gets or sets the stack of things the back button closes.
	/// </summary>
	[Inject]
	public IBackNavigation BackNavigation { get; set; } = null!;

	/// <summary>
	/// Gets or sets the JavaScript runtime that closes the keyboard.
	/// </summary>
	[Inject]
	public IJSRuntime JS { get; set; } = null!;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override void OnInitialized()
	{
		base.OnInitialized();

		// The back button is pressed outside the Blazor dispatcher, so the view model is changed through it.
		backRegistration = BackNavigation.Register(() => _ = InvokeAsync(ViewModel.Cancel));
	}
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

	#region Event Handlers
	/// <summary>
	/// Handles the back arrow.
	/// </summary>
	private void OnBack() => ViewModel.Cancel();

	/// <summary>
	/// Handles "Use my position".
	/// </summary>
	private void OnUseMyPosition() => ViewModel.UseMyPosition();

	/// <summary>
	/// Handles the submit of the search box: closes the keyboard, so that it does not cover the map, and searches.
	/// </summary>
	/// <returns>A task that completes when the result was shown.</returns>
	private async Task OnSubmitAsync()
	{
		await JS.CloseKeyboardAsync();
		await ViewModel.SearchAsync();
	}

	/// <summary>
	/// Handles the pick of a match in the list.
	/// </summary>
	/// <param name="match">The match.</param>
	private void OnPick(AddressMatch match) => ViewModel.Pick(match);

	/// <summary>
	/// Handles the request of the back button to close the list of matches.
	/// </summary>
	private void OnCloseResults() => ViewModel.CloseResults();
	#endregion
}
