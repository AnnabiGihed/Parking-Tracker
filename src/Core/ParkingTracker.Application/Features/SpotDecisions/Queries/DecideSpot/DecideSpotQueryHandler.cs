using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="DecideSpotQuery"/>: builds the decision context, then lets the pure decider ask for live data
///              step by step. Every failed source ends in a "can't verify" answer, never in a failed result.
/// </summary>
public sealed class DecideSpotQueryHandler : IQueryHandler<DecideSpotQuery, SpotDecision>
{
	#region Fields
	/// <summary>
	/// Factory that builds the decision context from the saved card and the reference data.
	/// </summary>
	private readonly IDecisionContextFactory contexts;

	/// <summary>
	/// Runner that drives the pure decider, fetching the live data each step asks for.
	/// </summary>
	private readonly IDecisionStepRunner runner;

	/// <summary>
	/// Tells whether the device is online.
	/// </summary>
	private readonly INetworkStatus network;

	/// <summary>
	/// Clock that supplies the current moment.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DecideSpotQueryHandler"/> class.
	/// </summary>
	/// <param name="contexts">Builds the card and street-list context.</param>
	/// <param name="runner">Fetches the live data the decision asks for.</param>
	/// <param name="network">Tells whether the device is online.</param>
	/// <param name="clock">Clock that supplies the current moment.</param>
	public DecideSpotQueryHandler(IDecisionContextFactory contexts, IDecisionStepRunner runner, INetworkStatus network, TimeProvider clock)
	{
		this.contexts = contexts;
		this.runner = runner;
		this.network = network;
		this.clock = clock;
	}
	#endregion

	#region IQueryHandler Implementation
	/// <inheritdoc />
	public async Task<Result<SpotDecision>> Handle(DecideSpotQuery request, CancellationToken cancellationToken)
	{
		var now = clock.GetLocalNow();
		var context = await contexts.CreateAsync(now, cancellationToken).ConfigureAwait(false);
		if (context.IsFailure)
		{
			return Result.Failure<SpotDecision>(context.Error);
		}

		var question = new SpotQuestion(context.Value, new GeoPoint(request.Latitude, request.Longitude), request.AccuracyMeters, now, network.IsOnline);
		var decision = await runner.RunAsync(SpotDecider.Begin(question), now, cancellationToken).ConfigureAwait(false);
		return Result.Success(decision);
	}
	#endregion
}
