using Pivot.Framework.Domain.Primitives;

namespace ParkingTracker.Domain.Features.Vehicles.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Identifies the vehicle profile of the user on the device; never <see cref="Guid.Empty"/>.
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public sealed record VehicleId(Guid Value) : StronglyTypedGuidId<VehicleId>(Value)
{
	#region Factory Methods
	/// <summary>
	/// Creates the identifier of a new vehicle profile.
	/// </summary>
	/// <returns>A new, never empty identifier.</returns>
	public static VehicleId New() => new(Guid.NewGuid());
	#endregion

	#region Operators
	/// <summary>
	/// Tells whether an identifier sorts before another.
	/// </summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> sorts before <paramref name="right"/>.</returns>
	public static bool operator <(VehicleId? left, VehicleId? right) => left is null ? right is not null : left.CompareTo(right!) < 0;

	/// <summary>
	/// Tells whether an identifier sorts before or equals another.
	/// </summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> does not sort after <paramref name="right"/>.</returns>
	public static bool operator <=(VehicleId? left, VehicleId? right) => left is null || left.CompareTo(right!) <= 0;

	/// <summary>
	/// Tells whether an identifier sorts after another.
	/// </summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> sorts after <paramref name="right"/>.</returns>
	public static bool operator >(VehicleId? left, VehicleId? right) => left is not null && left.CompareTo(right!) > 0;

	/// <summary>
	/// Tells whether an identifier sorts after or equals another.
	/// </summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> does not sort before <paramref name="right"/>.</returns>
	public static bool operator >=(VehicleId? left, VehicleId? right) => left is null ? right is null : left.CompareTo(right!) >= 0;
	#endregion
}
