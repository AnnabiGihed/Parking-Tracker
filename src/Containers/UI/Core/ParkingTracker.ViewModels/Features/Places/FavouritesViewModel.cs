using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Features.Places;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The list of favourite places that the side menu opens as a dialog (ADR-0030): each favourite is shown with its name and
///              address, a tap shows it on the map, the edit action opens the map picker on it, and "Add a favourite" starts the picker
///              for a new one. The dialog closes before the map or the picker opens, so that nothing covers them. There can be at most
///              <see cref="SavedPlaces.MaximumFavourites"/> favourites, and adding is refused with a reason once they exist.
///              The dialog is drawn by a <c>DialogBridge</c> through <see cref="IDialogViewModel"/>, so this class never references Radzen.
/// </summary>
public sealed class FavouritesViewModel : ViewModelBase, IDialogViewModel
{
	#region Constants
	/// <summary>
	/// What a favourite shows under its name while it has no address in words.
	/// </summary>
	public const string PositionText = "Saved from the map";
	#endregion

	#region Static Instances
	/// <summary>
	/// The reason shown while no favourite can be added.
	/// </summary>
	public static readonly string LimitReachedText = $"You can save at most {SavedPlaces.MaximumFavourites} favourite places. Remove one to add another.";
	#endregion

	#region Fields
	/// <summary>
	/// The saved places, which hold the favourites.
	/// </summary>
	private readonly PlacesViewModel places;

	/// <summary>
	/// The map picker that adds and changes favourites.
	/// </summary>
	private readonly PlacePickerViewModel picker;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FavouritesViewModel"/> class.
	/// </summary>
	/// <param name="places">The saved places, which hold the favourites.</param>
	/// <param name="picker">The map picker that adds and changes favourites.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public FavouritesViewModel(PlacesViewModel places, PlacePickerViewModel picker)
	{
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(picker);
		this.places = places;
		this.picker = picker;

		// Scoped like this view model and living as long as it does, so there is nothing to unsubscribe.
		places.Changed += OnPlacesChanged;
	}
	#endregion

	#region Properties
	/// <inheritdoc />
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Gets the saved favourites in the order they were saved.
	/// </summary>
	public IReadOnlyList<PlaceShortcut> Favourites => places.Favourites;

	/// <summary>
	/// Gets a value indicating whether no favourite is saved, so that the list shows its empty text.
	/// </summary>
	public bool IsEmpty => Favourites.Count == 0;

	/// <summary>
	/// Gets a value indicating whether another favourite can be added.
	/// </summary>
	public bool CanAdd => Favourites.Count < SavedPlaces.MaximumFavourites;

	/// <summary>
	/// Gets the reason why adding is disabled, or <see langword="null"/> while it is possible.
	/// </summary>
	public string? AddDisabledReason => CanAdd ? null : LimitReachedText;
	#endregion

	#region Public Methods
	/// <summary>
	/// Opens the list.
	/// </summary>
	public void Open()
	{
		if (IsOpen)
		{
			return;
		}

		IsOpen = true;
		NotifyChanged();
	}

	/// <inheritdoc />
	public void Close()
	{
		if (!IsOpen)
		{
			return;
		}

		IsOpen = false;
		NotifyChanged();
	}

	/// <summary>
	/// Gives the address line of a favourite.
	/// </summary>
	/// <param name="favourite">The favourite.</param>
	/// <returns>The address in words, or <see cref="PositionText"/> when it has none.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="favourite"/> is <see langword="null"/>.</exception>
	public static string AddressOf(PlaceShortcut favourite)
	{
		ArgumentNullException.ThrowIfNull(favourite);
		return string.IsNullOrEmpty(favourite.AddressText) ? PositionText : favourite.AddressText;
	}

	/// <summary>
	/// Closes the list and shows a favourite on the map, as a tap on the Home chip does.
	/// </summary>
	/// <param name="favourite">The favourite.</param>
	/// <returns>A task that completes when the place was shown.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="favourite"/> is <see langword="null"/>.</exception>
	public Task ShowAsync(PlaceShortcut favourite)
	{
		ArgumentNullException.ThrowIfNull(favourite);
		Close();
		return places.ShowAsync(favourite);
	}

	/// <summary>
	/// Closes the list and opens the map picker on a favourite, to rename, move or remove it.
	/// </summary>
	/// <param name="favourite">The favourite.</param>
	/// <exception cref="ArgumentNullException"><paramref name="favourite"/> is <see langword="null"/>.</exception>
	public void Edit(PlaceShortcut favourite)
	{
		ArgumentNullException.ThrowIfNull(favourite);
		Close();
		picker.Edit(favourite);
	}

	/// <summary>
	/// Closes the list and starts the map picker for a new favourite; nothing happens while the limit is reached.
	/// </summary>
	public void Add()
	{
		if (!CanAdd)
		{
			return;
		}

		Close();
		picker.Start(PlaceKind.Favourite);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Passes on a change of the saved places.
	/// </summary>
	/// <param name="sender">The places.</param>
	/// <param name="e">Not used.</param>
	private void OnPlacesChanged(object? sender, EventArgs e) => NotifyChanged();
	#endregion
}
