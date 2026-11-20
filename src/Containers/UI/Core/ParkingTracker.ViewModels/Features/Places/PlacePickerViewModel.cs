using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
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
/// Purpose     : The map mode that sets Home or Work, like the place pickers of Waze and Google Maps (ADR-0030). The user searches an
///              address (one match goes straight to the map, several are listed), uses the position, or taps the map; the chosen place
///              gets a pin that can be dragged, and only a tap on Save writes it, so nothing is ever saved before the user confirms.
///              While it is open the map is not moved to the position of the user, whether the position is followed or not; the
///              following state is kept and works again when the picker closes.
///              A place outside the Brussels box cannot be saved; the domain still validates, and its refusal is told in the toast.
///              A saved place is opened with its pin, so that it can be moved or removed. Every failure is told in the toast and never
///              thrown to the screen.
/// </summary>
public sealed class PlacePickerViewModel : ViewModelBase
{
	#region Constants
	/// <summary>
	/// The hint under the label of the chosen place.
	/// </summary>
	public const string DragHint = "Drag the pin to adjust";

	/// <summary>
	/// The label of a place the user chose by moving the pin or tapping the map; finding the address of a point is not done.
	/// </summary>
	public const string MovedLabel = "Pin moved on the map";

	/// <summary>
	/// The label of the position of the user.
	/// </summary>
	public const string MyPositionLabel = "My position";

	/// <summary>
	/// The label of a saved place that has no address in words.
	/// </summary>
	public const string SavedPositionLabel = "Saved from the map";

	/// <summary>
	/// What the user is told when the database refused to save or remove a place.
	/// </summary>
	public const string WriteFailedText = "Your places could not be saved. Please try again.";
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
	/// The screen that knows the position on screen.
	/// </summary>
	private readonly SpotCheckViewModel spot;

	/// <summary>
	/// The saved places, which are read again after a change.
	/// </summary>
	private readonly PlacesViewModel places;

	/// <summary>
	/// Shows the messages of the picker.
	/// </summary>
	private readonly ToastViewModel toast;

	/// <summary>
	/// The address in words that is saved with the chosen place; <see langword="null"/> when the place was chosen on the map.
	/// </summary>
	private string? candidateAddress;

	/// <summary>
	/// The text in the search box.
	/// </summary>
	private string searchText = string.Empty;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="PlacePickerViewModel"/> class.
	/// </summary>
	/// <param name="store">Port to the saved places.</param>
	/// <param name="sender">Sends the address search to the Application.</param>
	/// <param name="spot">The screen that knows the position on screen.</param>
	/// <param name="places">The saved places, which are read again after a change.</param>
	/// <param name="toast">Shows the messages of the picker.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public PlacePickerViewModel(IPlacesStore store, ISender sender, SpotCheckViewModel spot, PlacesViewModel places, ToastViewModel toast)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(sender);
		ArgumentNullException.ThrowIfNull(spot);
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(toast);
		this.store = store;
		this.sender = sender;
		this.spot = spot;
		this.places = places;
		this.toast = toast;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the step the picker is in.
	/// </summary>
	public PlacePickerState State { get; private set; } = PlacePickerState.Idle;

	/// <summary>
	/// Gets a value indicating whether the picker is open, so that the map is in picker mode.
	/// </summary>
	public bool IsActive => State != PlacePickerState.Idle;

	/// <summary>
	/// Gets the kind of place being set.
	/// </summary>
	public PlaceKind Kind { get; private set; } = PlaceKind.Home;

	/// <summary>
	/// Gets the title of the picker header.
	/// </summary>
	public string Title => Kind == PlaceKind.Home ? "Set home" : "Set work";

	/// <summary>
	/// Gets or sets the text in the search box; a change closes the list of matches.
	/// </summary>
	public string SearchText
	{
		get
		{
			return searchText;
		}

		set
		{
			if (string.Equals(searchText, value, StringComparison.Ordinal))
			{
				return;
			}

			searchText = value;
			CloseResults();
		}
	}

	/// <summary>
	/// Gets the matches the list offers; empty while the list is closed.
	/// </summary>
	public IReadOnlyList<AddressMatch> Matches { get; private set; } = [];

	/// <summary>
	/// Gets the place the pin is on, or <see langword="null"/> while no place is chosen.
	/// </summary>
	public GeoPoint? Candidate { get; private set; }

	/// <summary>
	/// Gets the words that name the chosen place.
	/// </summary>
	public string CandidateLabel { get; private set; } = string.Empty;

	/// <summary>
	/// Gets a value indicating whether a search is running.
	/// </summary>
	public bool IsBusy { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the confirm bar is shown: a place is chosen or being saved.
	/// </summary>
	public bool IsConfirmShown => State is PlacePickerState.Confirming or PlacePickerState.Saving;

	/// <summary>
	/// Gets a value indicating whether the chosen place lies outside the Brussels box, where it cannot be saved.
	/// </summary>
	public bool IsOutsideBrussels => Candidate is { } point && !IsInsideBrussels(point);

	/// <summary>
	/// Gets a value indicating whether Save is enabled: a place is chosen, inside Brussels, and nothing is being written.
	/// </summary>
	public bool CanSave => State == PlacePickerState.Confirming && Candidate is not null && !IsOutsideBrussels;

	/// <summary>
	/// Gets a value indicating whether the kind being set has a saved place, which can be removed.
	/// </summary>
	public bool HasSavedPlace => places.PlaceOf(Kind) is not null;
	#endregion

	#region Public Methods
	/// <summary>
	/// Opens the picker for a kind of place. A place that is already saved is shown with its pin, so that it can be moved or removed.
	/// </summary>
	/// <param name="kind">Home or work.</param>
	public void Start(PlaceKind kind)
	{
		if (State == PlacePickerState.Saving)
		{
			return;
		}

		Kind = kind;

		// Following the position would pan the map back to the user and hide the pin of the place being chosen.
		spot.SuspendFollowing();
		searchText = string.Empty;
		Matches = [];
		Candidate = null;
		CandidateLabel = string.Empty;
		candidateAddress = null;
		State = PlacePickerState.Searching;
		if (places.PlaceOf(kind) is { } saved)
		{
			Choose(saved.Point, string.IsNullOrEmpty(saved.AddressText) ? SavedPositionLabel : saved.AddressText, saved.AddressText);
			return;
		}

		NotifyChanged();
	}

	/// <summary>
	/// Searches the typed address: one match is chosen at once, several are listed to choose from.
	/// </summary>
	/// <param name="cancellationToken">Cancels the search.</param>
	/// <returns>A task that completes when the result was shown or the user was told why not.</returns>
	public async Task SearchAsync(CancellationToken cancellationToken = default)
	{
		var typed = SearchText.Trim();
		if (typed.Length == 0 || IsBusy || !IsActive || State == PlacePickerState.Saving)
		{
			return;
		}

		CloseResults();
		SetBusy(true);
		try
		{
			var result = await sender.Send(new SearchAddressQuery(typed), cancellationToken).ConfigureAwait(false);
			ShowSearchResult(result, typed);
		}
		finally
		{
			SetBusy(false);
		}
	}

	/// <summary>
	/// Chooses a match of the list and closes the list.
	/// </summary>
	/// <param name="match">The match the user picked.</param>
	public void Pick(AddressMatch match)
	{
		ArgumentNullException.ThrowIfNull(match);
		Matches = [];
		ChooseMatch(match, SearchText.Trim());
	}

	/// <summary>
	/// Chooses the position of the user, as the map screen knows it.
	/// </summary>
	public void UseMyPosition()
	{
		if (!IsActive || State == PlacePickerState.Saving)
		{
			return;
		}

		if (spot.CurrentPosition is not { } position)
		{
			toast.Show("No position yet.");
			return;
		}

		Matches = [];
		Choose(position, MyPositionLabel, null);
	}

	/// <summary>
	/// Moves the chosen place, because the pin was dragged or the map was tapped.
	/// </summary>
	/// <param name="point">The new place.</param>
	public void MoveCandidate(GeoPoint point)
	{
		if (!IsActive || State == PlacePickerState.Saving)
		{
			return;
		}

		Matches = [];
		Choose(point, MovedLabel, null);
	}

	/// <summary>
	/// Closes the list of matches and goes back to where the user was.
	/// </summary>
	public void CloseResults()
	{
		if (State != PlacePickerState.Results)
		{
			return;
		}

		Matches = [];
		State = Candidate is null ? PlacePickerState.Searching : PlacePickerState.Confirming;
		NotifyChanged();
	}

	/// <summary>
	/// Writes the chosen place, the only moment anything is saved; then the picker closes and the user is told.
	/// </summary>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the place was saved or the user was told why not.</returns>
	public async Task SaveAsync(CancellationToken cancellationToken = default)
	{
		if (!CanSave || Candidate is not { } point)
		{
			return;
		}

		var kind = Kind;
		State = PlacePickerState.Saving;
		NotifyChanged();
		try
		{
			var saved = await store.SavePlaceAsync(kind, point, candidateAddress, cancellationToken).ConfigureAwait(false);
			if (saved.IsFailure)
			{
				ReturnToConfirming(RefusalText(kind, saved.Error));
				return;
			}
		}
		catch (InvalidOperationException failure)
		{
			ReturnToConfirming(WriteFailureText(failure));
			return;
		}

		await places.LoadAsync(cancellationToken).ConfigureAwait(false);
		Leave();
		toast.Show($"{kind} saved.");
	}

	/// <summary>
	/// Removes the saved place of the kind being set; then the picker closes and the user is told.
	/// </summary>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the place was removed or the user was told why not.</returns>
	public async Task RemoveAsync(CancellationToken cancellationToken = default)
	{
		if (!IsActive || State == PlacePickerState.Saving)
		{
			return;
		}

		var kind = Kind;
		try
		{
			await store.ClearPlaceAsync(kind, cancellationToken).ConfigureAwait(false);
		}
		catch (InvalidOperationException failure)
		{
			toast.Show(WriteFailureText(failure));
			return;
		}

		await places.LoadAsync(cancellationToken).ConfigureAwait(false);
		Leave();
		toast.Show($"{kind} removed.");
	}

	/// <summary>
	/// Leaves the picker without saving anything, for the Cancel button and the back button.
	/// </summary>
	public void Cancel()
	{
		if (!IsActive || State == PlacePickerState.Saving)
		{
			return;
		}

		Leave();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Tells whether a point lies inside the Brussels box, edges included.
	/// </summary>
	/// <param name="point">The point.</param>
	/// <returns><see langword="true"/> when the point is inside.</returns>
	private static bool IsInsideBrussels(GeoPoint point)
	{
		var box = SavedPlaces.BrusselsBox;
		return point.Latitude >= box.South && point.Latitude <= box.North && point.Longitude >= box.West && point.Longitude <= box.East;
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
	/// Takes the first text that is not blank.
	/// </summary>
	/// <param name="first">The preferred text.</param>
	/// <param name="second">The text used when the first is blank.</param>
	/// <param name="last">The text used when both are blank.</param>
	/// <returns>The first text with content.</returns>
	private static string FirstText(string? first, string? second, string last)
	{
		if (!string.IsNullOrWhiteSpace(first))
		{
			return first;
		}

		return string.IsNullOrWhiteSpace(second) ? last : second;
	}

	/// <summary>
	/// Shows what a search found: the failure or "not found" in the toast, one match on the map, several in the list.
	/// </summary>
	/// <param name="result">The answer of the search.</param>
	/// <param name="typed">What the user typed.</param>
	private void ShowSearchResult(Result<SearchAddressResponse> result, string typed)
	{
		if (result.IsFailure)
		{
			toast.Show("Search failed — check your connection.");
			return;
		}

		var matches = result.Value.Matches;
		if (matches.Count == 0)
		{
			toast.Show("Address not found.");
		}
		else if (matches.Count == 1)
		{
			ChooseMatch(matches[0], typed);
		}
		else
		{
			Matches = matches;
			State = PlacePickerState.Results;
			NotifyChanged();
		}
	}

	/// <summary>
	/// Chooses a match: the pin goes on it and the confirm bar names it.
	/// </summary>
	/// <param name="match">The match.</param>
	/// <param name="typed">What the user typed, used when the match has no name.</param>
	private void ChooseMatch(AddressMatch match, string typed)
	{
		Choose(match.Point, FirstText(match.Label, match.DisplayName, typed), FirstText(match.DisplayName, match.Label, typed));
	}

	/// <summary>
	/// Puts the pin on a place and asks the user to confirm it.
	/// </summary>
	/// <param name="point">Where the place is.</param>
	/// <param name="label">The words that name it on the confirm bar.</param>
	/// <param name="address">The address in words to save with it; <see langword="null"/> when it has none.</param>
	private void Choose(GeoPoint point, string label, string? address)
	{
		Candidate = point;
		CandidateLabel = label;
		candidateAddress = address;
		State = PlacePickerState.Confirming;
		NotifyChanged();
	}

	/// <summary>
	/// Goes back to the confirm bar after a save that did not work, and tells the user why.
	/// </summary>
	/// <param name="message">The text for the toast.</param>
	private void ReturnToConfirming(string message)
	{
		State = PlacePickerState.Confirming;
		NotifyChanged();
		toast.Show(message);
	}

	/// <summary>
	/// Closes the picker and forgets the chosen place.
	/// </summary>
	private void Leave()
	{
		spot.ResumeFollowing();
		State = PlacePickerState.Idle;
		Candidate = null;
		CandidateLabel = string.Empty;
		candidateAddress = null;
		Matches = [];
		searchText = string.Empty;
		NotifyChanged();
	}

	/// <summary>
	/// Sets whether a search runs and tells the components.
	/// </summary>
	/// <param name="busy">Whether the search runs.</param>
	private void SetBusy(bool busy)
	{
		IsBusy = busy;
		NotifyChanged();
	}
	#endregion
}
