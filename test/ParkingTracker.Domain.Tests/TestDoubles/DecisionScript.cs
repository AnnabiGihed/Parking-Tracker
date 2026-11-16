using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.Zones;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Tests.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Drives a decision to its end with canned live data and records which sources it asked for, so that a test can
///              check both the answer and that no unneeded source was asked.
/// </summary>
public sealed class DecisionScript
{
	#region Properties
	/// <summary>
	/// Gets or sets the answer to the request for street surfaces.
	/// </summary>
	public Result<IReadOnlyList<ZoneSurface>>? Zones { get; set; }

	/// <summary>
	/// Gets or sets the answer to the request for the zone rule.
	/// </summary>
	public Result<IReadOnlyDictionary<string, ParkingRule>>? Rules { get; set; }

	/// <summary>
	/// Gets or sets the answer to the request for sector areas.
	/// </summary>
	public Result<IReadOnlyList<SectorMaille>>? Mailles { get; set; }

	/// <summary>
	/// Gets or sets the answer to the request for address points.
	/// </summary>
	public Result<IReadOnlyList<AddressPoint>>? Addresses { get; set; }

	/// <summary>
	/// Gets or sets the answer to the request for parking spaces.
	/// </summary>
	public Result<IReadOnlyList<ReservedSpace>>? Spaces { get; set; }

	/// <summary>
	/// Gets or sets the answer to the request for roadworks.
	/// </summary>
	public Result<IReadOnlyList<WorkItem>>? Works { get; set; }

	/// <summary>
	/// Gets the names of the sources asked, in order.
	/// </summary>
	public List<string> Asked { get; } = [];
	#endregion

	#region Public Methods
	/// <summary>
	/// Runs a decision to its end.
	/// </summary>
	/// <param name="first">The first step.</param>
	/// <returns>The answer.</returns>
	public SpotDecision Run(DecisionStep first)
	{
		var step = first;
		while (step is not DecisionStep.Decided)
		{
			step = Next(step);
		}

		return ((DecisionStep.Decided)step).Decision;
	}

	/// <summary>
	/// Creates a successful answer for the surfaces request.
	/// </summary>
	/// <param name="surfaces">The surfaces.</param>
	/// <returns>The result.</returns>
	public static Result<IReadOnlyList<ZoneSurface>> Ok(params ZoneSurface[] surfaces) => Result.Success<IReadOnlyList<ZoneSurface>>(surfaces);

	/// <summary>
	/// Creates a successful answer for the rules request.
	/// </summary>
	/// <param name="code">The zone code.</param>
	/// <param name="rule">The rule of the zone.</param>
	/// <returns>The result.</returns>
	public static Result<IReadOnlyDictionary<string, ParkingRule>> RuleOf(string code, ParkingRule rule)
	{
		return Result.Success<IReadOnlyDictionary<string, ParkingRule>>(new Dictionary<string, ParkingRule> { [code] = rule });
	}

	/// <summary>
	/// Creates a failed answer for any request.
	/// </summary>
	/// <typeparam name="T">The type of the data that was asked.</typeparam>
	/// <param name="message">The reason.</param>
	/// <returns>The failed result.</returns>
	public static Result<T> Failed<T>(string message) => Result.Failure<T>(new Error("Test.Failure", message));
	#endregion

	#region Private Helpers
	private DecisionStep Next(DecisionStep step)
	{
		switch (step)
		{
			case DecisionStep.NeedZones need:
				return need.Resume(Take("zones", Zones));
			case DecisionStep.NeedRule need:
				return need.Resume(Take("rule", Rules));
			case DecisionStep.NeedMailles need:
				return need.Resume(Take("mailles", Mailles));
			case DecisionStep.NeedAddresses need:
				return need.Resume(Take("addresses", Addresses));
			case DecisionStep.NeedSpaces need:
				return need.Resume(Take("spaces", Spaces));
			case DecisionStep.NeedWorks need:
				return need.Resume(Take("works", Works));
			default:
				throw new InvalidOperationException($"Unknown step {step}.");
		}
	}

	private Result<T> Take<T>(string name, Result<T>? canned)
	{
		Asked.Add(name);
		return canned ?? throw new InvalidOperationException($"The decision asked for '{name}', which this test did not expect.");
	}
	#endregion
}
