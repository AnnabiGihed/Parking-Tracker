using Microsoft.AspNetCore.Components;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Card;

namespace ParkingTracker.Shared.Features.Card.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The card dialog where the user enters the residents' card by hand: municipality, the City sector with its tolerance
///              codes or the areas printed on the card, the validity dates, the plate and the number. It saves through the
///              <see cref="CardViewModel"/>, which writes the card to the settings store. Scanning a photo is a later stage.
/// </summary>
public sealed partial class CardDialog : ViewModelComponent<CardViewModel>
{
	#region Event Handlers
	/// <summary>
	/// Handles a new municipality in the select.
	/// </summary>
	/// <param name="e">The change; its value is the identifier of the municipality.</param>
	/// <returns>A task that completes when the areas of the municipality were loaded.</returns>
	private Task OnMunicipalityChangedAsync(ChangeEventArgs e) => ViewModel.SelectMunicipalityAsync(e.Value?.ToString() ?? string.Empty);

	/// <summary>
	/// Handles a new sector in the select.
	/// </summary>
	/// <param name="e">The change; its value is the identifier of the sector.</param>
	private void OnSectorChanged(ChangeEventArgs e) => ViewModel.SelectSector(e.Value?.ToString() ?? string.Empty);

	/// <summary>
	/// Handles "Save card".
	/// </summary>
	/// <returns>A task that completes when the card was saved or the form was found incomplete.</returns>
	private async Task OnSaveAsync()
	{
		await ViewModel.SaveAsync();
	}

	/// <summary>
	/// Handles the close button.
	/// </summary>
	private void OnClose() => ViewModel.Close();
	#endregion
}
