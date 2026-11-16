using ParkingTracker.Application.Decisions;
using ParkingTracker.Infrastructure.ReferenceData;
using ParkingTracker.Infrastructure.Tests.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Parity;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Wires the production decision code of one recorded scenario to the recorded responses and the recorded clock.
/// </summary>
internal sealed class ParityHarness
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParityHarness"/> class.
	/// </summary>
	/// <param name="scenario">The recorded scenario.</param>
	public ParityHarness(ParityScenario scenario)
	{
		Scenario = scenario;
		Sources = new FixtureSources(scenario.Directory);
		var zone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Brussels");
		Clock = new FixedTimeProvider(scenario.Clock, zone);
		Checker = new OfficialListChecker(Sources);
		Contexts = new DecisionContextFactory(new StaticSettingsStore(scenario.Card), new ReferenceDataProvider(), Checker);
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the recorded scenario.
	/// </summary>
	public ParityScenario Scenario { get; }

	/// <summary>
	/// Gets the replayed sources.
	/// </summary>
	public FixtureSources Sources { get; }

	/// <summary>
	/// Gets the recorded clock.
	/// </summary>
	public FixedTimeProvider Clock { get; }

	/// <summary>
	/// Gets the official-document checker.
	/// </summary>
	public OfficialListChecker Checker { get; }

	/// <summary>
	/// Gets the factory of the decision context.
	/// </summary>
	public DecisionContextFactory Contexts { get; }

	/// <summary>
	/// Gets a runner that answers the steps of a decision from the replayed sources.
	/// </summary>
	public DecisionStepRunner Runner => new(Sources, Sources, Sources, Sources);

	/// <summary>
	/// Gets a provider that evaluates an area from the replayed sources.
	/// </summary>
	public AreaStatusProvider Areas => new(Sources, Sources, Sources);
	#endregion
}
