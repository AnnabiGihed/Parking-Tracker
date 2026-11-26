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
///              component before an entry opens its screen, so that two screens are never open at once. In a build that can sign in, the menu is one Account section: the account line
///              at the top, the section label that says where the entries are kept, and Sign out and Delete account at the bottom.
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

	/// <summary>
	/// The title of the account line while signed in.
	/// </summary>
	public const string AccountTitleText = "Account";

	/// <summary>
	/// The title of the account line while signed out.
	/// </summary>
	public const string SignInTitleText = "Sign in";

	/// <summary>
	/// The text of the account line while signed out.
	/// </summary>
	public const string SignInSubText = "Keep your data with your Google account";

	/// <summary>
	/// The text of the account line while signed in and the account id is not known yet.
	/// </summary>
	public const string SignedInText = "Signed in";

	/// <summary>
	/// The label above the entries while signed in.
	/// </summary>
	public const string SavedWithAccountText = "Saved with your account";

	/// <summary>
	/// The label above the entries while signed out or when the build cannot sign in.
	/// </summary>
	public const string OnThisPhoneText = "On this phone only";
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
		account.Changed += OnSourceChanged;
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

	/// <summary>
	/// Gets a value indicating whether the phone holds a session in a build that can sign in.
	/// </summary>
	public bool IsSignedIn => account.IsAvailable && account.IsSignedIn;

	/// <summary>
	/// Gets the title of the account line: "Account" while signed in, "Sign in" otherwise.
	/// </summary>
	public string AccountTitle => IsSignedIn ? AccountTitleText : SignInTitleText;

	/// <summary>
	/// Gets the text under the account line: "Signed in" with the short account id once it is known, or what signing in keeps.
	/// </summary>
	public string AccountText
	{
		get
		{
			if (!IsSignedIn)
			{
				return SignInSubText;
			}

			return account.Account is null ? SignedInText : $"{SignedInText} · {account.AccountIdText}";
		}
	}

	/// <summary>
	/// Gets the label above the entries: where what they hold is kept.
	/// </summary>
	public string SectionLabel => IsSignedIn ? SavedWithAccountText : OnThisPhoneText;

	/// <summary>
	/// Gets the last account failure shown in the menu, in plain words (for example a failed sign-out); empty when there is none.
	/// </summary>
	public string AccountError => account.Error;
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

	/// <summary>
	/// Opens the account sheet straight into the confirmation of the deletion of the account.
	/// </summary>
	/// <returns>A task that completes when the sheet is open.</returns>
	public Task OpenAccountToDeleteAsync() => account.OpenToDeleteAsync();

	/// <summary>
	/// Reads whether the phone is signed in, without showing a failure, so that the menu shows the right state when it opens.
	/// </summary>
	/// <returns>A task that completes when the state was read.</returns>
	public Task RefreshAccountAsync() => account.RefreshStateAsync();

	/// <summary>
	/// Signs this device out; the menu then shows the signed-out state.
	/// </summary>
	/// <returns>A task that completes when the phone is signed out or the reason it is not was recorded.</returns>
	public Task SignOutAsync() => account.SignOutAsync();
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
