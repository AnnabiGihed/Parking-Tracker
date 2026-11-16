using ParkingTracker.Application.Decisions;
using ParkingTracker.Application.Sources;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Geometry;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Queries;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="DecideSpotQuery"/>: builds the decision context, then lets the pure decider ask for live data
///              step by step. Every failed source ends in a "can't verify" answer, never in a failed result.
/// </summary>
public sealed class DecideSpotQueryHandler : IQueryHandler<DecideSpotQuery, SpotDecision>
{
	#region Fields
	private readonly IDecisionContextFactory contexts;
	private readonly IDecisionStepRunner runner;
	private readonly INetworkStatus network;
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DecideSpotQueryHandler"/> class.
	/// </summary>
	/// <param name="contexts">Builds the card and street-list context.</param>
	/// <param name="runner">Fetches the live data the decision asks for.</param>
	/// <param name="network">Tells whether the device is online.</param>
	/// <param name="clock">The clock.</param>
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
