namespace ParkingTracker.Domain.Addressing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The rules that apply to one street for one source (the card's sector or a tolerance agreement):
///              what the odd side and the even side cover.
/// </summary>
/// <param name="Label">Display label of the source, for example <c>your sector</c>.</param>
/// <param name="Odd">Rule of the odd side.</param>
/// <param name="Even">Rule of the even side.</param>
public sealed record RuleSet(string Label, SideRule Odd, SideRule Even)
{
	#region Public Methods
	/// <summary>
	/// Gets the rule of one side of the street.
	/// </summary>
	/// <param name="side">The side of the street.</param>
	/// <returns>The rule of that side.</returns>
	public SideRule RuleOf(StreetSide side) => side == StreetSide.Odd ? Odd : Even;

	/// <summary>
	/// Finds the first rule set that covers a house number on its side of the street.
	/// </summary>
	/// <param name="sets">The rule sets that apply to the street.</param>
	/// <param name="house">The house number.</param>
	/// <returns>The first set that covers the number, or <see langword="null"/> when none does.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="sets"/> or <paramref name="house"/> is <see langword="null"/>.</exception>
	public static RuleSet? FindCovering(IEnumerable<RuleSet> sets, HouseNumber house)
	{
		ArgumentNullException.ThrowIfNull(sets);
		ArgumentNullException.ThrowIfNull(house);
		return sets.FirstOrDefault(set => set.RuleOf(house.Side).Covers(house));
	}

	/// <summary>
	/// Finds the first condition in words ("côté parc"…) on one side of the street.
	/// </summary>
	/// <param name="sets">The rule sets that apply to the street.</param>
	/// <param name="side">The side of the street.</param>
	/// <returns>The first condition rule of that side, or <see langword="null"/> when there is none.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="sets"/> is <see langword="null"/>.</exception>
	public static SideRule? FindCondition(IEnumerable<RuleSet> sets, StreetSide side)
	{
		ArgumentNullException.ThrowIfNull(sets);
		return sets.Select(set => set.RuleOf(side)).FirstOrDefault(rule => rule.IsCondition);
	}
	#endregion
}
