using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.ViewModels.Features.Account;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.Vehicles;

namespace ParkingTracker.ViewModels.Features.Menu;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The side menu: the text under Home, Work, the favourite places, the vehicle and the parking card, taken from the places and the card line the screen
///              already holds, and what each entry opens (Home and Work start the map picker, favourite places open the list, My vehicle opens its dialog). The menu itself is closed by the
///              component before an entry opens its screen, so that two screens are never open at once.
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

	/// <summary>
	/// What the favourite places entry shows while none is saved.
	/// </summary>
	public const string NoFavouritesText = "None yet";
	#endregion

	#region Fields
	/// <summary>
	/// The saved places.
	/// </summary>
	private readonly PlacesViewModel places;

	/// <summary>
	/// The map picker that sets Home and Work.
	/// </summary>
	private readonly PlacePickerViewModel picker;

	/// <summary>
	/// The list of favourite places that its entry opens.
	/// </summary>
	private readonly FavouritesViewModel favourites;

	/// <summary>
	/// The saved vehicle and the dialog that edits it.
	/// </summary>
	private readonly VehicleViewModel vehicle;

	/// <summary>
	/// The saved card and the dialog that edits it.
	/// </summary>
	private readonly CardViewModel card;

	/// <summary>
	/// The account sheet that its entry opens.
	/// </summary>
	private readonly AccountViewModel account;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MenuViewModel"/> class.
	/// </summary>
	/// <param name="places">The saved places.</param>
	/// <param name="picker">The map picker that sets Home and Work.</param>
	/// <param name="favourites">The list of favourite places that its entry opens.</param>
	/// <param name="vehicle">The saved vehicle and the dialog that edits it.</param>
	/// <param name="card">The saved card and the dialog that edits it.</param>
	/// <param name="account">The account sheet that its entry opens.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public MenuViewModel(PlacesViewModel places, PlacePickerViewModel picker, FavouritesViewModel favourites, VehicleViewModel vehicle, CardViewModel card, AccountViewModel account)
	{
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(picker);
		ArgumentNullException.ThrowIfNull(favourites);
		ArgumentNullException.ThrowIfNull(vehicle);
		ArgumentNullException.ThrowIfNull(card);
		ArgumentNullException.ThrowIfNull(account);
		this.places = places;
		this.picker = picker;
		this.favourites = favourites;
		this.vehicle = vehicle;
		this.card = card;
		this.account = account;

		// All are scoped like this view model and live as long as it does, so there is nothing to unsubscribe.
		places.Changed += OnSourceChanged;
		vehicle.Changed += OnSourceChanged;
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
	/// Gets the text under the favourite places: how many are saved, or <see cref="NoFavouritesText"/>.
	/// </summary>
	public string FavouritesText => places.Favourites.Count == 0 ? NoFavouritesText : $"{places.Favourites.Count} saved";

	/// <summary>
	/// Gets the text under My vehicle: "Make Model · Year", or <see cref="NotSetText"/> when no vehicle is saved.
	/// </summary>
	public string VehicleText => vehicle.SummaryText;

	/// <summary>
	/// Gets the text under the parking card: the card summary line, or <see cref="NotSetText"/> when there is no card.
	/// </summary>
	public string CardText => card.Line.Dot == CardDot.None ? NotSetText : card.Line.Pill;

	/// <summary>
	/// Gets a value indicating whether the menu offers the account entry: only a build that can sign in with Google (one built with a Google Web client ID).
	/// </summary>
	public bool IsAccountVisible => account.IsAvailable;
	#endregion

	#region Public Methods
	/// <summary>
	/// Opens the map picker for a kind of place; a saved place is shown with its pin.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	public void OpenPlace(PlaceKind kind) => picker.Start(kind);

	/// <summary>
	/// Opens the list of favourite places.
	/// </summary>
	public void OpenFavourites() => favourites.Open();

	/// <summary>
	/// Opens the My vehicle dialog.
	/// </summary>
	public void OpenVehicle() => vehicle.Open();

	/// <summary>
	/// Opens the card dialog.
	/// </summary>
	/// <returns>A task that completes when the dialog opened.</returns>
	public Task OpenCardAsync() => card.OpenAsync();

	/// <summary>
	/// Opens the account sheet.
	/// </summary>
	/// <returns>A task that completes when the sheet is open and the account was read.</returns>
	public Task OpenAccountAsync() => account.OpenAsync();
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
	/// Passes on a change of the places, the vehicle or the card line.
	/// </summary>
	/// <param name="sender">The view model that changed.</param>
	/// <param name="e">Not used.</param>
	private void OnSourceChanged(object? sender, EventArgs e) => NotifyChanged();
	#endregion
}
