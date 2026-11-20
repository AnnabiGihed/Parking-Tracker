using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Features.Menu;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The side menu: the text under Home, Work and the parking card, taken from the places and the card line the screen
///              already holds, and what each entry opens. The menu itself is closed by the component before an entry opens its screen,
///              so that two dialogs are never open at once.
/// </summary>
public sealed class MenuViewModel : ViewModelBase
{
	#region Constants
	/// <summary>
	/// What an entry shows while its place or card is not set.
	/// </summary>
	public const string NotSetText = "Not set";

	/// <summary>
	/// What a place shows when it was saved from a position on the map and has no address in words.
	/// </summary>
	public const string PositionText = "Saved from the map";
	#endregion

	#region Fields
	/// <summary>
	/// The saved places and the dialog that edits them.
	/// </summary>
	private readonly PlacesViewModel places;

	/// <summary>
	/// The saved card and the dialog that edits it.
	/// </summary>
	private readonly CardViewModel card;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MenuViewModel"/> class.
	/// </summary>
	/// <param name="places">The saved places and the dialog that edits them.</param>
	/// <param name="card">The saved card and the dialog that edits it.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public MenuViewModel(PlacesViewModel places, CardViewModel card)
	{
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(card);
		this.places = places;
		this.card = card;

		// Both are scoped like this view model and live as long as it does, so there is nothing to unsubscribe.
		places.Changed += OnSourceChanged;
		card.Changed += OnSourceChanged;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the text under Home: the saved address, or <see cref="NotSetText"/>.
	/// </summary>
	public string HomeText => PlaceText(places.Home);

	/// <summary>
	/// Gets the text under Work: the saved address, or <see cref="NotSetText"/>.
	/// </summary>
	public string WorkText => PlaceText(places.Work);

	/// <summary>
	/// Gets the text under the parking card: the card summary line, or <see cref="NotSetText"/> when there is no card.
	/// </summary>
	public string CardText => card.Line.Dot == CardDot.None ? NotSetText : card.Line.Pill;
	#endregion

	#region Public Methods
	/// <summary>
	/// Opens the place dialog for a kind of place.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	public void OpenPlace(PlaceKind kind) => places.Open(kind);

	/// <summary>
	/// Opens the card dialog.
	/// </summary>
	/// <returns>A task that completes when the dialog opened.</returns>
	public Task OpenCardAsync() => card.OpenAsync();
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the text of a place entry.
	/// </summary>
	/// <param name="place">The saved place, or <see langword="null"/>.</param>
	/// <returns>The address, <see cref="PositionText"/> when it has none, or <see cref="NotSetText"/> when the place is not saved.</returns>
	private static string PlaceText(PlaceShortcut? place)
	{
		if (place is null)
		{
			return NotSetText;
		}

		return string.IsNullOrEmpty(place.AddressText) ? PositionText : place.AddressText;
	}

	/// <summary>
	/// Passes on a change of the places or the card line.
	/// </summary>
	/// <param name="sender">The view model that changed.</param>
	/// <param name="e">Not used.</param>
	private void OnSourceChanged(object? sender, EventArgs e) => NotifyChanged();
	#endregion
}
