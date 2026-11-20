using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;

namespace ParkingTracker.ViewModels.Features.Places;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The saved Home and Work places behind the shortcut chips and the side menu: it reads them, and a tap on a saved place
///              moves the map there and checks the spot like a search result. Setting, changing or removing a place is done in the map
///              picker (<see cref="PlacePickerViewModel"/>), which asks this view model to read the places again. A failed read is told
///              in the toast and never thrown to the screen.
/// </summary>
public sealed class PlacesViewModel : ViewModelBase
{
	#region Constants
	/// <summary>
	/// What the user is told when the saved places could not be read for a reason other than unreadable data.
	/// </summary>
	public const string ReadFailedText = "Your places could not be read. Please try again.";
	#endregion

	#region Fields
	/// <summary>
	/// Port to the saved places.
	/// </summary>
	private readonly IPlacesStore store;

	/// <summary>
	/// The screen that shows the place and knows the position on screen.
	/// </summary>
	private readonly SpotCheckViewModel spot;

	/// <summary>
	/// Shows the messages of the chips.
	/// </summary>
	private readonly ToastViewModel toast;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="PlacesViewModel"/> class; the saved places are read by <see cref="LoadAsync"/>.
	/// </summary>
	/// <param name="store">Port to the saved places.</param>
	/// <param name="spot">The screen that shows the place and knows the position on screen.</param>
	/// <param name="toast">Shows the messages of the chips.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public PlacesViewModel(IPlacesStore store, SpotCheckViewModel spot, ToastViewModel toast)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(spot);
		ArgumentNullException.ThrowIfNull(toast);
		this.store = store;
		this.spot = spot;
		this.toast = toast;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the saved home, or <see langword="null"/> when none is saved.
	/// </summary>
	public PlaceShortcut? Home { get; private set; }

	/// <summary>
	/// Gets the saved work place, or <see langword="null"/> when none is saved.
	/// </summary>
	public PlaceShortcut? Work { get; private set; }
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads the saved places. Call it when the screen starts.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task that completes when the places were read or the user was told that they cannot be.</returns>
	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			Apply(await store.GetPlacesAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (InvalidOperationException failure)
		{
			// Covers SavedDataUnreadableException, whose message tells the user what to do, and a refused read of the database.
			Apply([]);
			toast.Show(failure is SavedDataUnreadableException ? failure.Message : ReadFailedText);
		}
	}

	/// <summary>
	/// Gets the saved place of a kind.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <returns>The place, or <see langword="null"/> when it is not set.</returns>
	public PlaceShortcut? PlaceOf(PlaceKind kind) => kind == PlaceKind.Home ? Home : Work;

	/// <summary>
	/// Handles a tap on a chip whose place is saved: shows the place on the map and checks the spot there.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <returns>A task that completes when the place was shown; at once when the place is not saved.</returns>
	public Task ShowAsync(PlaceKind kind)
	{
		return PlaceOf(kind) is { } place ? spot.ShowSearchResultAsync(place.Point) : Task.CompletedTask;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Takes over the places that were read and tells the components.
	/// </summary>
	/// <param name="places">The saved places.</param>
	private void Apply(IReadOnlyList<PlaceShortcut> places)
	{
		Home = places.FirstOrDefault(place => place.Kind == PlaceKind.Home);
		Work = places.FirstOrDefault(place => place.Kind == PlaceKind.Work);
		NotifyChanged();
	}
	#endregion
}
