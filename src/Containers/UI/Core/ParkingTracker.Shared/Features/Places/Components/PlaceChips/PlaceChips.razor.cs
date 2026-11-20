using Microsoft.AspNetCore.Components;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.ViewModels.Features.Places;

namespace ParkingTracker.Shared.Features.Places.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The Home and Work chips under the search bar: a tap on a saved place shows it on the map, a tap on a place that is not
///              set opens the map picker. A saved place is changed or removed from the side menu.
/// </summary>
public sealed partial class PlaceChips : ViewModelComponent<PlacesViewModel>
{
	#region Injected Services
	/// <summary>
	/// Gets or sets the map picker that a chip of a place that is not set opens.
	/// </summary>
	[Inject]
	public PlacePickerViewModel Picker { get; set; } = null!;
	#endregion

	#region Lifecycle Methods
	/// <inheritdoc />
	protected override async Task OnInitializedAsync()
	{
		await ViewModel.LoadAsync();
	}
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles a tap on the Home chip.
	/// </summary>
	/// <returns>A task that completes when the place was shown.</returns>
	private Task OnGoHomeAsync() => GoAsync(PlaceKind.Home);

	/// <summary>
	/// Handles a tap on the Work chip.
	/// </summary>
	/// <returns>A task that completes when the place was shown.</returns>
	private Task OnGoWorkAsync() => GoAsync(PlaceKind.Work);
	#endregion

	#region Private Helpers
	/// <summary>
	/// Shows a saved place on the map, or opens the picker when the place is not set.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <returns>A task that completes when the place was shown.</returns>
	private Task GoAsync(PlaceKind kind)
	{
		if (ViewModel.PlaceOf(kind) is null)
		{
			Picker.Start(kind);
			return Task.CompletedTask;
		}

		return ViewModel.ShowAsync(kind);
	}
	#endregion
}
