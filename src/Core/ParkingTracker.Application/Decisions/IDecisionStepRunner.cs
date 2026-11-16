using ParkingTracker.Domain.Decisions;

namespace ParkingTracker.Application.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Carries a decision to its end: fetches the live data each step asks for through the source ports and hands the
///              outcome back, whether it is data or a failure.
/// </summary>
public interface IDecisionStepRunner
{
	#region Methods
	/// <summary>
	/// Runs a decision until it is made.
	/// </summary>
	/// <param name="firstStep">The first step, from <see cref="SpotDecider.Begin"/>.</param>
	/// <param name="now">The current moment, passed to the sources that filter by date.</param>
	/// <param name="cancellationToken">Cancels the fetching.</param>
	/// <returns>The answer; a failed source is part of the answer, not an exception.</returns>
	Task<SpotDecision> RunAsync(DecisionStep firstStep, DateTimeOffset now, CancellationToken cancellationToken);
	#endregion
}
