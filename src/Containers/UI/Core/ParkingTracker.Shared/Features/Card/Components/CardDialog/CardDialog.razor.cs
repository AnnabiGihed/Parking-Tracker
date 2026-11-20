using System.Globalization;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Shared.Features.Shared.Components;
using ParkingTracker.ViewModels.Features.Card;

namespace ParkingTracker.Shared.Features.Card.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The card dialog where the user enters the residents' card by hand: municipality, the City sector with its tolerance
///              codes or the areas printed on the card, the validity dates, the plate and the number. "Scan my card" asks the
///              <see cref="CardViewModel"/> to read a photo and shows its status line. It saves through the
///              <see cref="CardViewModel"/>, which writes the card to the settings store.
/// </summary>
public sealed partial class CardDialog : ViewModelComponent<CardViewModel>
{
	#region Constants
	/// <summary>
	/// The format in which the view model holds a date: the value of a date input, so it does not depend on the culture.
	/// </summary>
	private const string StoredDateFormat = "yyyy-MM-dd";

	/// <summary>
	/// The format in which a date picker shows its date: the short date of the phone's culture, without the time of day the
	/// default format of Radzen adds and that did not fit in the field.
	/// </summary>
	private const string DisplayDateFormat = "d";
	#endregion

	#region Properties
	/// <summary>
	/// Gets the municipalities as identifier and label, the label showing the Dutch name next to the French one when they differ.
	/// </summary>
	private IEnumerable<KeyValuePair<string, string>> MunicipalityOptions => ViewModel.Municipalities
		.Select(municipality => new KeyValuePair<string, string>(municipality.Id, BilingualName(municipality.Name, municipality.NameNl)));

	/// <summary>
	/// Gets the City sectors as identifier and label, the label showing the Dutch name next to the French one when they differ.
	/// </summary>
	private IEnumerable<KeyValuePair<string, string>> SectorOptions => ViewModel.Sectors
		.Select(sector => new KeyValuePair<string, string>(sector.Id, BilingualName(sector.Name, sector.NameNl)));
	#endregion

	#region Event Handlers
	/// <summary>
	/// Handles a new municipality in the drop-down.
	/// </summary>
	/// <param name="municipalityId">The identifier of the municipality.</param>
	/// <returns>A task that completes when the areas of the municipality were loaded.</returns>
	private Task OnMunicipalityChangedAsync(string? municipalityId) => ViewModel.SelectMunicipalityAsync(municipalityId ?? string.Empty);

	/// <summary>
	/// Handles a new sector in the drop-down.
	/// </summary>
	/// <param name="sectorId">The identifier of the sector.</param>
	private void OnSectorChanged(string? sectorId) => ViewModel.SelectSector(sectorId ?? string.Empty);

	/// <summary>
	/// Handles a change of the ticked areas.
	/// </summary>
	/// <param name="chosen">The zone codes that are ticked now.</param>
	private void OnMaillesChanged(IEnumerable<string>? chosen) => ApplySelection(ViewModel.SelectedMailles, chosen);

	/// <summary>
	/// Handles a change of the ticked border zones.
	/// </summary>
	/// <param name="chosen">The zone codes that are ticked now.</param>
	private void OnLimsChanged(IEnumerable<string>? chosen) => ApplySelection(ViewModel.SelectedLims, chosen);

	/// <summary>
	/// Handles a change of the ticked tolerance agreements.
	/// </summary>
	/// <param name="chosen">The codes that are ticked now.</param>
	private void OnTolerancesChanged(IEnumerable<string>? chosen) => ApplySelection(ViewModel.SelectedTolerances, chosen);

	/// <summary>
	/// Handles a new "valid from" date.
	/// </summary>
	/// <param name="date">The date picked, or <see langword="null"/> when it was cleared.</param>
	private void OnValidFromChanged(DateTime? date) => ViewModel.ValidFrom = FormatDate(date);

	/// <summary>
	/// Handles a new "valid until" date.
	/// </summary>
	/// <param name="date">The date picked, or <see langword="null"/> when it was cleared.</param>
	private void OnValidToChanged(DateTime? date) => ViewModel.ValidTo = FormatDate(date);

	/// <summary>
	/// Handles each keystroke in the plate field and keeps the plate in capitals.
	/// </summary>
	/// <param name="typed">The text in the field.</param>
	private void OnPlateChanged(string? typed) => ViewModel.Plate = PlateInput.Normalise(typed);

	/// <summary>
	/// Handles "Scan my card".
	/// </summary>
	/// <returns>A task that completes when the form was filled or the failure was shown.</returns>
	private Task OnScanAsync() => ViewModel.ScanAsync();

	/// <summary>
	/// Handles "Save card".
	/// </summary>
	/// <param name="card">The view model the form is bound to; not used.</param>
	/// <returns>A task that completes when the card was saved or the form was found incomplete.</returns>
	private async Task OnSaveAsync(CardViewModel card)
	{
		await ViewModel.SaveAsync();
	}

	/// <summary>
	/// Handles the close button.
	/// </summary>
	private void OnClose() => ViewModel.Close();
	#endregion

	#region Private Helpers
	/// <summary>
	/// Joins the French and the Dutch name, or returns the French one when both are the same.
	/// </summary>
	/// <param name="name">The French name.</param>
	/// <param name="nameNl">The Dutch name.</param>
	/// <returns>The label to show.</returns>
	private static string BilingualName(string name, string nameNl) => string.Equals(nameNl, name, StringComparison.Ordinal) ? name : name + " / " + nameNl;

	/// <summary>
	/// Makes a selection of the view model equal to what the list shows, through the view model's own toggle.
	/// </summary>
	/// <param name="selection">The selection held by the view model.</param>
	/// <param name="chosen">The codes ticked in the list; <see langword="null"/> when none is.</param>
	private void ApplySelection(ISet<string> selection, IEnumerable<string>? chosen)
	{
		var ticked = chosen?.ToHashSet(StringComparer.Ordinal) ?? [];
		foreach (var code in selection.Where(code => !ticked.Contains(code)).ToList())
		{
			ViewModel.Toggle(selection, code, false);
		}

		foreach (var code in ticked.Where(code => !selection.Contains(code)).ToList())
		{
			ViewModel.Toggle(selection, code, true);
		}
	}

	/// <summary>
	/// Reads a date the view model holds as text.
	/// </summary>
	/// <param name="text">The date as <c>yyyy-MM-dd</c>, or empty.</param>
	/// <returns>The date, or <see langword="null"/> when the text is empty or not a date.</returns>
	private static DateTime? ParseDate(string text) =>
		DateTime.TryParseExact(text, StoredDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

	/// <summary>
	/// Writes a date the way the view model holds it.
	/// </summary>
	/// <param name="date">The date, or <see langword="null"/>.</param>
	/// <returns>The date as <c>yyyy-MM-dd</c>, or an empty text for no date.</returns>
	private static string FormatDate(DateTime? date) => date?.ToString(StoredDateFormat, CultureInfo.InvariantCulture) ?? string.Empty;
	#endregion
}
