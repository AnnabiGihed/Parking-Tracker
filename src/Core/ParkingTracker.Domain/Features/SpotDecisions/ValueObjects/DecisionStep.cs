using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One step of a decision in progress: either the answer, or a request for live data together with what to do with
///              it. The decision stays pure: the caller fetches what a step asks for (in any way it likes) and passes the
///              outcome, success or failure, to <c>Resume</c>.
/// </summary>
public abstract record DecisionStep
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DecisionStep"/> class. Private, so that the cases stay the nested ones.
	/// </summary>
	private DecisionStep()
	{
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The decision is made.
	/// </summary>
	/// <param name="Decision">Final decision for the spot.</param>
	public sealed record Decided(SpotDecision Decision) : DecisionStep;

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The street surfaces of an area are needed.
	/// </summary>
	/// <param name="Box">Bounding box of the area to fetch.</param>
	/// <param name="Resume">Continues the decision with the surfaces or the reason they could not be fetched.</param>
	public sealed record NeedZones(GeoBox Box, Func<Result<IReadOnlyList<ZoneSurface>>, DecisionStep> Resume) : DecisionStep;

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The live parking rule of a zone is needed.
	/// </summary>
	/// <param name="Code">The zone code.</param>
	/// <param name="Resume">Continues the decision with the rules by zone code or the reason they could not be fetched.</param>
	public sealed record NeedRule(string Code, Func<Result<IReadOnlyDictionary<string, ParkingRule>>, DecisionStep> Resume) : DecisionStep;

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The sector areas (mailles) of an area are needed.
	/// </summary>
	/// <param name="Box">Bounding box of the area to fetch.</param>
	/// <param name="Resume">Continues the decision with the areas or the reason they could not be fetched.</param>
	public sealed record NeedMailles(GeoBox Box, Func<Result<IReadOnlyList<SectorMaille>>, DecisionStep> Resume) : DecisionStep;

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The official address points of an area are needed.
	/// </summary>
	/// <param name="Box">Bounding box of the area to fetch.</param>
	/// <param name="Resume">Continues the decision with the address points or the reason they could not be fetched.</param>
	public sealed record NeedAddresses(GeoBox Box, Func<Result<IReadOnlyList<AddressPoint>>, DecisionStep> Resume) : DecisionStep;

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The individual parking spaces of an area are needed.
	/// </summary>
	/// <param name="Box">Bounding box of the area to fetch.</param>
	/// <param name="Resume">Continues the decision with the spaces or the reason they could not be fetched.</param>
	public sealed record NeedSpaces(GeoBox Box, Func<Result<IReadOnlyList<ReservedSpace>>, DecisionStep> Resume) : DecisionStep;

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The roadworks and traffic events running in an area are needed.
	/// </summary>
	/// <param name="Box">Bounding box of the area to fetch.</param>
	/// <param name="Resume">Continues the decision with the items or the reason they could not be fetched.</param>
	public sealed record NeedWorks(GeoBox Box, Func<Result<IReadOnlyList<WorkItem>>, DecisionStep> Resume) : DecisionStep;
	#endregion
}
