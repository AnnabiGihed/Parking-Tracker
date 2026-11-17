using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.Shared.Features.SpotCheck.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The search bar at the top of the screen: a text box that looks an address up through the
///              <see cref="SearchViewModel"/> on submit, with the card button at its end.
/// </summary>
public sealed partial class SearchBar : ViewModelComponent<SearchViewModel>
{
	#region Event Handlers
	/// <summary>
	/// Handles the submit of the search form.
	/// </summary>
	/// <returns>A task that completes when the result was shown.</returns>
	private Task OnSubmitAsync() => ViewModel.SubmitAsync();
	#endregion
}
