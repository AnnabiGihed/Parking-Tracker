using Pivot.Framework.Domain.Primitives;

namespace ParkingTracker.Domain.Features.Accounts.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Identifies one user account of the server; never <see cref="Guid.Empty"/>.
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public sealed record UserAccountId(Guid Value) : StronglyTypedGuidId<UserAccountId>(Value)
{
	#region Factory Methods
	/// <summary>
	/// Creates the identifier of a new account.
	/// </summary>
	/// <returns>A new, never empty identifier.</returns>
	public static UserAccountId New() => new(Guid.NewGuid());
	#endregion

	#region Operators
	/// <summary>
	/// Tells whether an identifier sorts before another.
	/// </summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> sorts before <paramref name="right"/>.</returns>
	public static bool operator <(UserAccountId? left, UserAccountId? right) => left is null ? right is not null : left.CompareTo(right!) < 0;

	/// <summary>
	/// Tells whether an identifier sorts before or equals another.
	/// </summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> does not sort after <paramref name="right"/>.</returns>
	public static bool operator <=(UserAccountId? left, UserAccountId? right) => left is null || left.CompareTo(right!) <= 0;

	/// <summary>
	/// Tells whether an identifier sorts after another.
	/// </summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> sorts after <paramref name="right"/>.</returns>
	public static bool operator >(UserAccountId? left, UserAccountId? right) => left is not null && left.CompareTo(right!) > 0;

	/// <summary>
	/// Tells whether an identifier sorts after or equals another.
	/// </summary>
	/// <param name="left">The first identifier.</param>
	/// <param name="right">The second identifier.</param>
	/// <returns><see langword="true"/> when <paramref name="left"/> does not sort before <paramref name="right"/>.</returns>
	public static bool operator >=(UserAccountId? left, UserAccountId? right) => left is null ? right is null : left.CompareTo(right!) >= 0;
	#endregion
}
