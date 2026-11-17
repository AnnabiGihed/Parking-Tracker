using ParkingTracker.Application.Features.Sectors.Queries.GetOfficialListStatus;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Features.SpotCheck;
using Pivot.Framework.Application.Abstractions.Messaging;

namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the emulator test (<c>test/ParkingTracker.E2E.Tests</c>) asks of the running app: save a card, read it back,
///              decide a place, search free spots, read the official-list check and the permission and network state. It goes through
///              the same ports and queries as the screens, so the test proves the real path. The class is plain C#, so unit tests cover
///              it; the host registers it and exposes it to the web view as <c>window.ptTest</c> only in a build made with
///              <c>-p:EnableTestHooks=true</c>. A failure is thrown, which the test sees as a rejected promise.
/// </summary>
public sealed class TestHookService
{
	#region Fields
	/// <summary>
	/// Sends the queries to the Application.
	/// </summary>
	private readonly ISender sender;

	/// <summary>
	/// The saved settings, where the card goes.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// The card view model, whose line is shown again after the card changed.
	/// </summary>
	private readonly CardViewModel card;

	/// <summary>
	/// The location permissions of the device.
	/// </summary>
	private readonly ILocationPermissions permissions;

	/// <summary>
	/// The connection status of the device.
	/// </summary>
	private readonly INetworkStatus network;

	/// <summary>
	/// The main screen, whose state the test reads when a check fails.
	/// </summary>
	private readonly SpotCheckViewModel spot;

	/// <summary>
	/// Clock for the age of the last fix.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="TestHookService"/> class.
	/// </summary>
	/// <param name="sender">Sends the queries to the Application.</param>
	/// <param name="settings">The saved settings, where the card goes.</param>
	/// <param name="card">The card view model, whose line is shown again after the card changed.</param>
	/// <param name="permissions">The location permissions of the device.</param>
	/// <param name="network">The connection status of the device.</param>
	/// <param name="spot">The main screen, whose state the test reads when a check fails.</param>
	/// <param name="clock">Clock for the age of the last fix.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public TestHookService(ISender sender, ISettingsStore settings, CardViewModel card, ILocationPermissions permissions, INetworkStatus network, SpotCheckViewModel spot, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(sender);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(card);
		ArgumentNullException.ThrowIfNull(permissions);
		ArgumentNullException.ThrowIfNull(network);
		ArgumentNullException.ThrowIfNull(spot);
		ArgumentNullException.ThrowIfNull(clock);
		this.spot = spot;
		this.clock = clock;
		this.sender = sender;
		this.settings = settings;
		this.card = card;
		this.permissions = permissions;
		this.network = network;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Saves a card given in the JSON of <c>pt.card</c> and announces it like the card form does, so the screen checks the spot
	/// again with the new card.
	/// </summary>
	/// <param name="json">The card as the first version of the app wrote it.</param>
	/// <returns>A task that completes when the card is saved and announced.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="json"/> is not a card.</exception>
	public async Task SetCardAsync(string json)
	{
		var parsed = SettingsJson.ParseCard(json) ?? throw new ArgumentException("The text is not a card.", nameof(json));
		var first = await settings.GetCardAsync().ConfigureAwait(false) is null;
		await settings.SaveCardAsync(parsed).ConfigureAwait(false);
		await card.AnnounceSavedCardAsync(first).ConfigureAwait(false);
	}

	/// <summary>
	/// Reads the saved card back in the JSON of <c>pt.card</c>.
	/// </summary>
	/// <returns>A task with the JSON, or <see langword="null"/> when no card is saved.</returns>
	public async Task<string?> GetCardAsync()
	{
		return await settings.GetCardAsync().ConfigureAwait(false) is { } saved ? SettingsJson.SerializeCard(saved) : null;
	}

	/// <summary>
	/// Decides a place as a check of the screen does.
	/// </summary>
	/// <param name="latitude">Degrees north.</param>
	/// <param name="longitude">Degrees east.</param>
	/// <param name="accuracyMetres">The accuracy of the position in metres, or <see langword="null"/> for a pin.</param>
	/// <returns>The answer.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the query failed, for example for coordinates out of range.</exception>
	public async Task<DecisionAnswer> DecideAsync(double latitude, double longitude, double? accuracyMetres)
	{
		var result = await sender.Send(new DecideSpotQuery(latitude, longitude, accuracyMetres), CancellationToken.None).ConfigureAwait(false);
		if (result.IsFailure)
		{
			throw new InvalidOperationException(result.Error.Message);
		}

		var decision = result.Value;
		return new DecisionAnswer(decision.Status.ToString().ToLowerInvariant(), decision.Title, decision.Detail, decision.Tariff, decision.Notes);
	}

	/// <summary>
	/// Searches the nearest spots where the card works.
	/// </summary>
	/// <param name="latitude">Degrees north.</param>
	/// <param name="longitude">Degrees east.</param>
	/// <param name="count">How many spots at most.</param>
	/// <returns>The spots, nearest first.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the query failed.</exception>
	public async Task<IReadOnlyList<FreeSpotAnswer>> FreeSpotsAsync(double latitude, double longitude, int count)
	{
		var result = await sender.Send(new FindFreeSpotsQuery(latitude, longitude, count), CancellationToken.None).ConfigureAwait(false);
		if (result.IsFailure)
		{
			throw new InvalidOperationException(result.Error.Message);
		}

		return result.Value.Select(spot => new FreeSpotAnswer(spot.Label, spot.Metres, spot.Street, spot.Commune)).ToArray();
	}

	/// <summary>
	/// Reads the outcome of the check of the City's official sector list, refreshing it when it is stale.
	/// </summary>
	/// <returns>The state and, when it is not <c>ok</c>, the problem.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the query failed.</exception>
	public async Task<OfficialListAnswer> OfficialListAsync()
	{
		var result = await sender.Send(new GetOfficialListStatusQuery(), CancellationToken.None).ConfigureAwait(false);
		if (result.IsFailure)
		{
			throw new InvalidOperationException(result.Error.Message);
		}

		return result.Value.Status is { } status
			? new OfficialListAnswer(status.State.ToString().ToLowerInvariant(), status.Problem)
			: new OfficialListAnswer(OfficialListAnswer.NotCheckedState, null);
	}

	/// <summary>
	/// Reads what the device allows and whether it is online.
	/// </summary>
	/// <returns>The foreground and background location permission and the connection.</returns>
	public async Task<PlatformAnswer> PlatformStatusAsync()
	{
		var status = await permissions.GetStatusAsync().ConfigureAwait(false);
		return new PlatformAnswer(
			status.Foreground.ToString().ToLowerInvariant(),
			status.Background.ToString().ToLowerInvariant(),
			network.IsOnline);
	}

	/// <summary>
	/// Reads what the main screen holds: whether the position is followed, the last accepted fix and its age, the headline and the
	/// explanation of the status block, and whether it is busy.
	/// </summary>
	/// <returns>The state of the screen.</returns>
	public ScreenStateAnswer ScreenState()
	{
		var me = spot.Me is { } position
			? new MyPositionAnswer(
				position.Fix.Point.Latitude,
				position.Fix.Point.Longitude,
				position.Fix.AccuracyMetres,
				Math.Round((clock.GetUtcNow() - position.Fix.Time).TotalSeconds, 1))
			: null;
		var busy = spot.IsChecking || spot.Chips.Any(chip => chip.Kind == ChipKind.Busy);
		return new ScreenStateAnswer(spot.IsTracking, me, spot.Status.Title, spot.Status.Detail, busy);
	}
	#endregion
}
