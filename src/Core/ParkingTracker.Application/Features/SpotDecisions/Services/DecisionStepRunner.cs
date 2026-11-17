using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;

namespace ParkingTracker.Application.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Fetches, one step at a time, the live data a decision asks for. The sources are asked in the order the decision
///              needs them, and only when it needs them, so a source that is not needed cannot make the answer fail.
/// </summary>
public sealed class DecisionStepRunner : IDecisionStepRunner
{
	#region Fields
	/// <summary>
	/// Port to the parking.brussels WFS service: zones, reserved spaces and mailles.
	/// </summary>
	private readonly IParkingBrusselsClient parkingBrussels;

	/// <summary>
	/// Port to the parking rules service.
	/// </summary>
	private readonly IParkingRulesClient rules;

	/// <summary>
	/// Port to the address points service.
	/// </summary>
	private readonly IAddressPointsClient addresses;

	/// <summary>
	/// Port to the mobility WFS layers: roadworks and traffic events.
	/// </summary>
	private readonly IMobilityLayersClient layers;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DecisionStepRunner"/> class.
	/// </summary>
	/// <param name="parkingBrussels">The parking.brussels port.</param>
	/// <param name="rules">The parking rules port.</param>
	/// <param name="addresses">The address points port.</param>
	/// <param name="layers">The roadworks and events port.</param>
	public DecisionStepRunner(IParkingBrusselsClient parkingBrussels, IParkingRulesClient rules, IAddressPointsClient addresses, IMobilityLayersClient layers)
	{
		this.parkingBrussels = parkingBrussels;
		this.rules = rules;
		this.addresses = addresses;
		this.layers = layers;
	}
	#endregion

	#region IDecisionStepRunner Implementation
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException"><paramref name="firstStep"/> is <see langword="null"/>.</exception>
	public async Task<SpotDecision> RunAsync(DecisionStep firstStep, DateTimeOffset now, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(firstStep);
		var current = firstStep;
		while (current is not DecisionStep.Decided)
		{
			current = await FetchAsync(current, now, cancellationToken).ConfigureAwait(false);
		}

		return ((DecisionStep.Decided)current).Decision;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Fetches the data one step asks for.
	/// </summary>
	/// <param name="step">The step to answer.</param>
	/// <param name="now">The current moment.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The step with its data filled in.</returns>
	private async Task<DecisionStep> FetchAsync(DecisionStep step, DateTimeOffset now, CancellationToken cancellationToken)
	{
		return step switch
		{
			DecisionStep.NeedZones need => need.Resume(await parkingBrussels.GetZonesAsync(need.Box, cancellationToken).ConfigureAwait(false)),
			DecisionStep.NeedRule need => need.Resume(await rules.GetRulesAsync(need.Code, now, cancellationToken).ConfigureAwait(false)),
			DecisionStep.NeedMailles need => need.Resume(await parkingBrussels.GetMaillesAsync(need.Box, cancellationToken).ConfigureAwait(false)),
			DecisionStep.NeedAddresses need => need.Resume(await addresses.GetAddressPointsAsync(need.Box, cancellationToken).ConfigureAwait(false)),
			DecisionStep.NeedSpaces need => need.Resume(await parkingBrussels.GetSpacesAsync(need.Box, cancellationToken).ConfigureAwait(false)),
			DecisionStep.NeedWorks need => need.Resume(await layers.GetWorksAsync(need.Box, now, cancellationToken).ConfigureAwait(false)),
			_ => throw new InvalidOperationException($"Unknown decision step {step.GetType().Name}."),
		};
	}
	#endregion
}
