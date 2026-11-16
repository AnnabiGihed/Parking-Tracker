using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Zones;

namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A house number along a street surface and the rule set of the card that covers it, if any.
/// </summary>
/// <param name="Address">The address.</param>
/// <param name="Covering">The rule set that covers the house number, or <see langword="null"/> when the card does not cover it.</param>
public sealed record NearAddress(AssignedAddress Address, RuleSet? Covering)
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether the card covers the house number.
	/// </summary>
	public bool IsCovered => Covering is not null;
	#endregion
}
