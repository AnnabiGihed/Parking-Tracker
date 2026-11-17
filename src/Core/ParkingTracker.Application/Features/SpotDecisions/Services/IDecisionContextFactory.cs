using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds what a decision needs besides the place: the saved card, how its municipality works, the street lists of
///              its sector and the outcome of the official-document check.
/// </summary>
public interface IDecisionContextFactory
{
	#region Methods
	/// <summary>
	/// Creates the context for a decision.
	/// </summary>
	/// <param name="now">The current moment.</param>
	/// <param name="cancellationToken">Cancels the official-document check.</param>
	/// <returns>The context, or a failure when the shipped reference data is missing or invalid.</returns>
	Task<Result<DecisionContext>> CreateAsync(DateTimeOffset now, CancellationToken cancellationToken);
	#endregion
}
