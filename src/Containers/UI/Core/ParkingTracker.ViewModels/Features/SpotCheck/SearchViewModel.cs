using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Application.Abstractions.Messaging;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The search bar: sends the typed text to the Application (only when it is submitted, as the usage policy of the
///              search service forbids search-as-you-type). One match is shown on the map with a pin and a spot check; several open a
///              list to choose from, which closes when a match is picked, when the text changes or when the map is tapped. "Not found"
///              and a failed search are told to the user in the toast.
/// </summary>
public sealed class SearchViewModel : ViewModelBase
{
	#region Fields
	/// <summary>
	/// Sends the search to the Application.
	/// </summary>
	private readonly ISender sender;

	/// <summary>
	/// The screen that shows the result.
	/// </summary>
	private readonly SpotCheckViewModel spot;

	/// <summary>
	/// Shows the messages of the search.
	/// </summary>
	private readonly ToastViewModel toast;

	/// <summary>
	/// The text in the search box.
	/// </summary>
	private string text = string.Empty;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SearchViewModel"/> class.
	/// </summary>
	/// <param name="sender">Sends the search to the Application.</param>
	/// <param name="spot">The screen that shows the result.</param>
	/// <param name="toast">Shows the messages of the search.</param>
	public SearchViewModel(ISender sender, SpotCheckViewModel spot, ToastViewModel toast)
	{
		this.sender = sender;
		this.spot = spot;
		this.toast = toast;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets or sets the text in the search box.
	/// </summary>
	public string Text
	{
		get
		{
			return text;
		}

		set
		{
			if (string.Equals(text, value, StringComparison.Ordinal))
			{
				return;
			}

			text = value;
			CloseList();
		}
	}

	/// <summary>
	/// Gets the matches of the last search that the list offers; empty while the list is closed.
	/// </summary>
	public IReadOnlyList<AddressMatch> Matches { get; private set; } = [];

	/// <summary>
	/// Gets a value indicating whether the list of matches is open.
	/// </summary>
	public bool IsListOpen { get; private set; }
	#endregion

	#region Public Methods
	/// <summary>
	/// Handles the submit of the search box.
	/// </summary>
	/// <param name="cancellationToken">Cancels the search.</param>
	/// <returns>A task that completes when the result was shown or the user was told.</returns>
	public async Task SubmitAsync(CancellationToken cancellationToken = default)
	{
		var typed = Text.Trim();
		if (typed.Length == 0)
		{
			return;
		}

		CloseList();
		var result = await sender.Send(new SearchAddressQuery(typed), cancellationToken).ConfigureAwait(false);
		if (result.IsFailure)
		{
			toast.Show("Search failed — check your connection.");
			return;
		}

		var matches = result.Value.Matches;
		if (matches.Count == 0)
		{
			toast.Show("Address not found.");
			return;
		}

		if (matches.Count == 1)
		{
			await spot.ShowSearchResultAsync(matches[0].Point).ConfigureAwait(false);
			return;
		}

		Matches = matches;
		IsListOpen = true;
		NotifyChanged();
	}

	/// <summary>
	/// Handles the pick of a match in the list: closes the list and shows the match on the map.
	/// </summary>
	/// <param name="match">The match the user picked.</param>
	/// <returns>A task that completes when the match was shown.</returns>
	public Task PickAsync(AddressMatch match)
	{
		CloseList();
		return spot.ShowSearchResultAsync(match.Point);
	}

	/// <summary>
	/// Closes the list of matches, for example on the back button, a tap on the map or a change of the text.
	/// </summary>
	public void CloseList()
	{
		if (!IsListOpen)
		{
			return;
		}

		IsListOpen = false;
		Matches = [];
		NotifyChanged();
	}
	#endregion
}
