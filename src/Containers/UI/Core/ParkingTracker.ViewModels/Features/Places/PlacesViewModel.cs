using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.Errors;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Features.Places;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The Home and Work shortcut chips and their dialog: a tap on a saved place moves the map there and checks the spot like a
///              search result, a tap on a place that is not set opens the dialog, where the user types an address, uses the position on
///              screen, or removes the place. Every failure (address not found, search failed, outside Brussels, saved data that cannot be
///              read) is told in the toast and never thrown to the screen.
/// </summary>
public sealed class PlacesViewModel : ViewModelBase
{
	#region Constants
	/// <summary>
	/// What the user is told when the database refused to save or remove a place.
	/// </summary>
	public const string WriteFailedText = "Your places could not be saved. Please try again.";

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
	/// Sends the address search to the Application.
	/// </summary>
	private readonly ISender sender;

	/// <summary>
	/// The screen that shows the place and knows the position on screen.
	/// </summary>
	private readonly SpotCheckViewModel spot;

	/// <summary>
	/// Shows the messages of the chips and the dialog.
	/// </summary>
	private readonly ToastViewModel toast;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="PlacesViewModel"/> class; the saved places are read by <see cref="LoadAsync"/>.
	/// </summary>
	/// <param name="store">Port to the saved places.</param>
	/// <param name="sender">Sends the address search to the Application.</param>
	/// <param name="spot">The screen that shows the place and knows the position on screen.</param>
	/// <param name="toast">Shows the messages of the chips and the dialog.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public PlacesViewModel(IPlacesStore store, ISender sender, SpotCheckViewModel spot, ToastViewModel toast)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(sender);
		ArgumentNullException.ThrowIfNull(spot);
		ArgumentNullException.ThrowIfNull(toast);
		this.store = store;
		this.sender = sender;
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

	/// <summary>
	/// Gets a value indicating whether the dialog is open.
	/// </summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Gets the kind of place the dialog edits.
	/// </summary>
	public PlaceKind EditingKind { get; private set; } = PlaceKind.Home;

	/// <summary>
	/// Gets or sets the address typed in the dialog.
	/// </summary>
	public string AddressText { get; set; } = string.Empty;

	/// <summary>
	/// Gets a value indicating whether a search or a save is running.
	/// </summary>
	public bool IsBusy { get; private set; }

	/// <summary>
	/// Gets the place the dialog edits, or <see langword="null"/> when it is not set.
	/// </summary>
	public PlaceShortcut? Editing => Of(EditingKind);
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
	/// Handles a tap on a chip: shows a saved place on the map, or opens the dialog when it is not set.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <returns>A task that completes when the place was shown.</returns>
	public Task GoAsync(PlaceKind kind)
	{
		if (Of(kind) is { } place)
		{
			return spot.ShowSearchResultAsync(place.Point);
		}

		Open(kind);
		return Task.CompletedTask;
	}

	/// <summary>
	/// Opens the dialog for a kind of place, with an empty address box.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	public void Open(PlaceKind kind)
	{
		EditingKind = kind;
		AddressText = string.Empty;
		IsOpen = true;
		NotifyChanged();
	}

	/// <summary>
	/// Closes the dialog.
	/// </summary>
	public void Close()
	{
		IsOpen = false;
		NotifyChanged();
	}

	/// <summary>
	/// Searches the typed address and saves the best match as the place being edited.
	/// </summary>
	/// <param name="cancellationToken">Cancels the search.</param>
	/// <returns>A task that completes when the place was saved or the user was told why not.</returns>
	public async Task SaveFromAddressAsync(CancellationToken cancellationToken = default)
	{
		var text = AddressText.Trim();
		if (text.Length == 0 || IsBusy)
		{
			return;
		}

		SetBusy(true);
		try
		{
			var result = await sender.Send(new SearchAddressQuery(text), cancellationToken).ConfigureAwait(false);
			if (result.IsFailure)
			{
				toast.Show("Search failed — check your connection.");
			}
			else if (result.Value.Match is not { } match)
			{
				toast.Show("Address not found.");
			}
			else
			{
				await SaveAsync(match.Point, string.IsNullOrWhiteSpace(match.DisplayName) ? text : match.DisplayName, cancellationToken).ConfigureAwait(false);
			}
		}
		finally
		{
			SetBusy(false);
		}
	}

	/// <summary>
	/// Saves the position or pin that is on screen as the place being edited.
	/// </summary>
	/// <param name="cancellationToken">Cancels the save.</param>
	/// <returns>A task that completes when the place was saved or the user was told why not.</returns>
	public async Task SaveFromMyPositionAsync(CancellationToken cancellationToken = default)
	{
		if (spot.CurrentPosition is not { } at)
		{
			toast.Show("No position yet.");
			return;
		}

		await SaveAsync(at, null, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Removes the place being edited and closes the dialog.
	/// </summary>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the place was removed or the user was told why not.</returns>
	public async Task RemoveAsync(CancellationToken cancellationToken = default)
	{
		var kind = EditingKind;
		try
		{
			await store.ClearPlaceAsync(kind, cancellationToken).ConfigureAwait(false);
			Apply(await store.GetPlacesAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (InvalidOperationException failure)
		{
			toast.Show(WriteFailureText(failure));
			return;
		}

		IsOpen = false;
		NotifyChanged();
		toast.Show($"{kind} removed.");
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Saves a point as the place being edited, then closes the dialog and tells the user; a refused place is told in the toast.
	/// </summary>
	/// <param name="point">Where the place is.</param>
	/// <param name="addressText">The address in words; <see langword="null"/> when unknown.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the place was saved or the user was told why not.</returns>
	private async Task SaveAsync(GeoPoint point, string? addressText, CancellationToken cancellationToken)
	{
		var kind = EditingKind;
		try
		{
			var saved = await store.SavePlaceAsync(kind, point, addressText, cancellationToken).ConfigureAwait(false);
			if (saved.IsFailure)
			{
				toast.Show(RefusalText(kind, saved.Error));
				return;
			}

			Apply(await store.GetPlacesAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (InvalidOperationException failure)
		{
			toast.Show(WriteFailureText(failure));
			return;
		}

		IsOpen = false;
		NotifyChanged();
		toast.Show($"{kind} saved.");
	}

	/// <summary>
	/// Words why a write of the saved places failed: the message of unreadable saved data, otherwise a general text.
	/// </summary>
	/// <param name="failure">What the store threw.</param>
	/// <returns>The text for the toast.</returns>
	private static string WriteFailureText(InvalidOperationException failure)
	{
		return failure is SavedDataUnreadableException ? failure.Message : WriteFailedText;
	}

	/// <summary>
	/// Words the reason why a place was refused.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <param name="error">The error of the domain.</param>
	/// <returns>The text for the toast.</returns>
	private static string RefusalText(PlaceKind kind, Error error)
	{
		return string.Equals(error.Code, SavedPlacesErrors.PointOutsideBrussels.Code, StringComparison.Ordinal)
			? $"{kind} has to be inside the Brussels-Capital Region."
			: error.Message;
	}

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

	/// <summary>
	/// Gets the saved place of a kind.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	/// <returns>The place, or <see langword="null"/> when it is not set.</returns>
	private PlaceShortcut? Of(PlaceKind kind) => kind == PlaceKind.Home ? Home : Work;

	/// <summary>
	/// Sets whether a search or save runs and tells the components.
	/// </summary>
	/// <param name="busy">Whether the work runs.</param>
	private void SetBusy(bool busy)
	{
		IsBusy = busy;
		NotifyChanged();
	}
	#endregion
}
