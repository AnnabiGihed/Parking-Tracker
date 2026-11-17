using ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Application.Abstractions.Messaging;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The search bar: sends the typed text to the Application and, when an address is found, shows it on the map with
///              a pin and a spot check. "Not found" and a failed search are told to the user in the toast.
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
	public string Text { get; set; } = string.Empty;
	#endregion

	#region Public Methods
	/// <summary>
	/// Handles the submit of the search box.
	/// </summary>
	/// <param name="cancellationToken">Cancels the search.</param>
	/// <returns>A task that completes when the result was shown or the user was told.</returns>
	public async Task SubmitAsync(CancellationToken cancellationToken = default)
	{
		var text = Text.Trim();
		if (text.Length == 0)
		{
			return;
		}

		var result = await sender.Send(new SearchAddressQuery(text), cancellationToken).ConfigureAwait(false);
		if (result.IsFailure)
		{
			toast.Show("Search failed — check your connection.");
			return;
		}

		if (result.Value.Match is not { } match)
		{
			toast.Show("Address not found.");
			return;
		}

		await spot.ShowSearchResultAsync(match.Point).ConfigureAwait(false);
	}
	#endregion
}
