using System.Globalization;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Application.Abstractions.Messaging.Commands;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.ParkingDetection.Commands.HandleParked;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="HandleParkedCommand"/> as the first version of the app did when it noticed parking: decides the
///              spot, saves the car with the headline of the answer, and notifies "icon + headline" with the explanation, the tariff
///              and, when parking is not free in a known zone, the nearest free spot. A failed search for a free spot only leaves
///              the suggestion out; a failed decision (broken reference data) fails the command and notifies nothing.
/// </summary>
public sealed class HandleParkedCommandHandler : ICommandHandler<HandleParkedCommand>
{
	#region Constants
	/// <summary>
	/// The accuracy used when the device reported none or zero, as the first version of the app did.
	/// </summary>
	private const double DefaultAccuracyMetres = 10;
	#endregion

	#region Fields
	/// <summary>
	/// Sends the spot decision and the free-spot search.
	/// </summary>
	private readonly ISender sender;

	/// <summary>
	/// The saved settings, where the parked car goes.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// Shows the alert.
	/// </summary>
	private readonly INotifier notifier;

	/// <summary>
	/// Clock that stamps the saved car.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="HandleParkedCommandHandler"/> class.
	/// </summary>
	/// <param name="sender">Sends the spot decision and the free-spot search.</param>
	/// <param name="settings">The saved settings, where the parked car goes.</param>
	/// <param name="notifier">Shows the alert.</param>
	/// <param name="clock">Clock that stamps the saved car.</param>
	public HandleParkedCommandHandler(ISender sender, ISettingsStore settings, INotifier notifier, TimeProvider clock)
	{
		this.sender = sender;
		this.settings = settings;
		this.notifier = notifier;
		this.clock = clock;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result> Handle(HandleParkedCommand request, CancellationToken cancellationToken)
	{
		var place = new GeoPoint(request.Latitude, request.Longitude);
		var accuracy = request.AccuracyMeters is > 0 ? Math.Min(request.AccuracyMeters.Value, SpotDecider.MaxGpsAccuracyMetres) : DefaultAccuracyMetres;
		var decided = await sender.Send(new DecideSpotQuery(place.Latitude, place.Longitude, accuracy), cancellationToken).ConfigureAwait(false);
		if (decided.IsFailure)
		{
			return Result.Failure(decided.Error);
		}

		var decision = decided.Value;
		await settings.SaveCarAsync(new ParkedCar(place, clock.GetLocalNow(), decision.Headline, string.Empty)).ConfigureAwait(false);
		var body = await BuildBodyAsync(decision, place, cancellationToken).ConfigureAwait(false);
		await notifier.NotifyAsync($"{IconOf(decision.Status)} {decision.Headline}", body, cancellationToken).ConfigureAwait(false);
		return Result.Success();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Gets the icon in front of the title of the alert.
	/// </summary>
	/// <param name="status">The colour of the answer.</param>
	/// <returns>A check mark for free, a warning sign for paid, an information sign otherwise.</returns>
	private static string IconOf(SpotStatus status) => status switch
	{
		SpotStatus.Free => "✅",
		SpotStatus.Pay => "⚠️",
		_ => "ℹ️",
	};

	/// <summary>
	/// Builds the text of the alert: the explanation, then the tariff and the nearest free spot when parking is not free.
	/// </summary>
	/// <param name="decision">The answer for the place.</param>
	/// <param name="place">Where the car stands.</param>
	/// <param name="cancellationToken">Cancels the search.</param>
	/// <returns>A task with the text.</returns>
	private async Task<string> BuildBodyAsync(SpotDecision decision, GeoPoint place, CancellationToken cancellationToken)
	{
		var body = decision.Detail;
		if (decision.Status == SpotStatus.Free)
		{
			return body;
		}

		if (!string.IsNullOrEmpty(decision.Tariff))
		{
			body += $" · {decision.Tariff}";
		}

		if (decision.Zone is not null && await FindNearestFreeSpotAsync(place, cancellationToken).ConfigureAwait(false) is { } spot)
		{
			body += string.Create(CultureInfo.InvariantCulture, $" · Nearest free spot for you: {spot.Label}, {Math.Round(spot.Metres, MidpointRounding.AwayFromZero)} m.");
		}

		return body;
	}

	/// <summary>
	/// Searches the nearest spot where the card works.
	/// </summary>
	/// <param name="place">Where the search starts.</param>
	/// <param name="cancellationToken">Cancels the search.</param>
	/// <returns>A task with the spot, or <see langword="null"/> when there is none or the search failed.</returns>
	private async Task<FreeSpot?> FindNearestFreeSpotAsync(GeoPoint place, CancellationToken cancellationToken)
	{
		var found = await sender.Send(new FindFreeSpotsQuery(place.Latitude, place.Longitude, 1), cancellationToken).ConfigureAwait(false);
		return found.IsSuccess && found.Value.Count > 0 ? found.Value[0] : null;
	}
	#endregion
}
