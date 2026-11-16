namespace ParkingTracker.Domain.Addressing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : How a rule treats one side of a street.
/// </summary>
public enum SideRuleKind
{
	/// <summary>
	/// The whole side is covered.
	/// </summary>
	All,

	/// <summary>
	/// No number of the side is covered.
	/// </summary>
	None,

	/// <summary>
	/// The side is covered only under a condition written in words (<c>check: côté parc</c>); it never covers a number by itself.
	/// </summary>
	Condition,

	/// <summary>
	/// The numbers in the listed ranges are covered.
	/// </summary>
	Ranges,
}
