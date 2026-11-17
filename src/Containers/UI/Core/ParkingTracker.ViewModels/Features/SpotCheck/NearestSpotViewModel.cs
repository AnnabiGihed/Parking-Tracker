using System.Globalization;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;
using Pivot.Framework.Application.Abstractions.Messaging;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The nearest-spot block of the bottom sheet: searches the nearest spots the card covers, keeps the answer of the
///              latest search only, and opens the best one in Waze or Google Maps through the link launcher.
/// </summary>
public sealed class NearestSpotViewModel : ViewModelBase
{
	#region Constants
	/// <summary>
	/// The walking speed used for the estimate, in metres per minute.
	/// </summary>
	private const double WalkingMetresPerMinute = 80;
	#endregion

	#region Fields
	/// <summary>
	/// Sends the search to the Application.
	/// </summary>
	private readonly ISender sender;

	/// <summary>
	/// Opens the navigation links.
	/// </summary>
	private readonly ILinkLauncher launcher;

	/// <summary>
	/// Clock that stamps the searches.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// Counts the searches, so that an older answer cannot replace a newer one.
	/// </summary>
	private int sequence;

	/// <summary>
	/// The last search that started.
	/// </summary>
	private SpotSearch? lastSearch;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="NearestSpotViewModel"/> class.
	/// </summary>
	/// <param name="sender">Sends the search to the Application.</param>
	/// <param name="launcher">Opens the navigation links.</param>
	/// <param name="clock">Clock that stamps the searches.</param>
	public NearestSpotViewModel(ISender sender, ILinkLauncher launcher, TimeProvider clock)
	{
		this.sender = sender;
		this.launcher = launcher;
		this.clock = clock;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets what the block shows.
	/// </summary>
	public NearestSpotState State { get; private set; }

	/// <summary>
	/// Gets the best spot, or <see langword="null"/> unless <see cref="State"/> is <see cref="NearestSpotState.Found"/>.
	/// </summary>
	public FreeSpot? Best { get; private set; }

	/// <summary>
	/// Gets the other spots found, nearest first.
	/// </summary>
	public IReadOnlyList<FreeSpot> Others { get; private set; } = [];

	/// <summary>
	/// Gets why the search failed, when <see cref="State"/> is <see cref="NearestSpotState.Failed"/>.
	/// </summary>
	public string FailureMessage { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the line under the best spot: that the card covers it, how far it is and how long the walk takes.
	/// </summary>
	public string BestSubtitle => Best is null
		? string.Empty
		: string.Create(CultureInfo.InvariantCulture, $"Free with your card · {Round(Best.Metres)} m · about {WalkMinutes(Best.Metres)} min on foot");

	/// <summary>
	/// Gets the line that lists the other spots, or an empty text when there are none.
	/// </summary>
	public string OthersText => Others.Count == 0
		? string.Empty
		: "Also nearby: " + string.Join(" · ", Others.Select(spot => string.Create(CultureInfo.InvariantCulture, $"{spot.Label} ({Round(spot.Metres)} m)")));
	#endregion

	#region Public Methods
	/// <summary>
	/// Tells whether a new search is due for a place just checked.
	/// </summary>
	/// <param name="at">The place that was just checked.</param>
	/// <param name="source">Whether the place is the user's position or a pin.</param>
	/// <returns><see langword="true"/> when the search should run, by <see cref="TrackingRules.IsSpotSearchDue"/>.</returns>
	public bool IsSearchDue(GeoPoint at, CheckSource source)
	{
		return TrackingRules.IsSpotSearchDue(lastSearch, at, clock.GetUtcNow(), source);
	}

	/// <summary>
	/// Searches the nearest spots the card covers around a place and shows them; the spots and the failure of the previous search are forgotten at once.
	/// </summary>
	/// <param name="from">The place to search around.</param>
	/// <param name="cancellationToken">Cancels the search.</param>
	/// <returns>The best spot; <see langword="null"/> when nothing was found, the search failed, or a newer search replaced this one.</returns>
	public async Task<FreeSpot?> ShowAsync(GeoPoint from, CancellationToken cancellationToken = default)
	{
		var mine = ++sequence;
		lastSearch = new SpotSearch(from, clock.GetUtcNow());
		State = NearestSpotState.Loading;
		Best = null;
		Others = [];
		FailureMessage = string.Empty;
		NotifyChanged();
		var result = await sender.Send(new FindFreeSpotsQuery(from.Latitude, from.Longitude), cancellationToken).ConfigureAwait(false);
		if (mine != sequence)
		{
			return null;
		}

		if (result.IsFailure)
		{
			State = NearestSpotState.Failed;
			FailureMessage = $"Could not search right now ({result.Error.Message}).";
		}
		else
		{
			Apply(result.Value);
		}

		NotifyChanged();
		return Best;
	}

	/// <summary>
	/// Hides the block, drops the answer of a search that is still running and forgets the spots and the failure of the last one.
	/// </summary>
	public void Hide()
	{
		sequence++;
		State = NearestSpotState.Hidden;
		Best = null;
		Others = [];
		FailureMessage = string.Empty;
		NotifyChanged();
	}

	/// <summary>
	/// Opens the best spot in Waze.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	public Task OpenWazeAsync(CancellationToken cancellationToken = default)
	{
		return Best is null ? Task.CompletedTask : launcher.OpenAsync(NavigationLinks.Waze(Best.Point), cancellationToken);
	}

	/// <summary>
	/// Opens the best spot in Google Maps with driving directions.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task that completes when the link was handed to the launcher.</returns>
	public Task OpenGoogleMapsAsync(CancellationToken cancellationToken = default)
	{
		return Best is null ? Task.CompletedTask : launcher.OpenAsync(NavigationLinks.GoogleMapsDriving(Best.Point), cancellationToken);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Takes the spots of a finished search.
	/// </summary>
	/// <param name="spots">The spots, nearest first.</param>
	private void Apply(IReadOnlyList<FreeSpot> spots)
	{
		if (spots.Count == 0)
		{
			State = NearestSpotState.NoneFound;
			return;
		}

		State = NearestSpotState.Found;
		Best = spots[0];
		Others = [.. spots.Skip(1)];
	}

	/// <summary>
	/// Rounds a distance to whole metres the way the first version of the screen did.
	/// </summary>
	/// <param name="metres">The distance.</param>
	/// <returns>The whole number of metres.</returns>
	private static double Round(double metres) => Math.Round(metres, MidpointRounding.AwayFromZero);

	/// <summary>
	/// Estimates the walk to a spot, at least one minute.
	/// </summary>
	/// <param name="metres">The distance.</param>
	/// <returns>The minutes.</returns>
	private static double WalkMinutes(double metres) => Math.Max(1, Round(metres / WalkingMetresPerMinute));
	#endregion
}
