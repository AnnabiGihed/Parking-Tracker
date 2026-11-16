using ParkingTracker.Domain.Naming;

namespace ParkingTracker.Domain.Addressing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Everything the lists say about one street: its rules in the sector and its tolerance agreements.
/// </summary>
/// <param name="Index">Position of the street in <see cref="SectorModel.Streets"/>.</param>
/// <param name="FrenchName">Street name in French.</param>
/// <param name="DutchName">Street name in Dutch.</param>
/// <param name="Odd">Sector rule of the odd side; not covered when the street is not in the sector.</param>
/// <param name="Even">Sector rule of the even side; not covered when the street is not in the sector.</param>
/// <param name="InSector">Whether the street is in the sector's own street list.</param>
/// <param name="Tolerances">The tolerance agreements that list the street.</param>
public sealed record StreetRules(
	int Index,
	string FrenchName,
	string DutchName,
	SideRule Odd,
	SideRule Even,
	bool InSector,
	IReadOnlyList<ToleranceRule> Tolerances)
{
	#region Constants
	/// <summary>
	/// Normalised name of the City of Brussels as parking.brussels writes the municipality; sector rules apply only there.
	/// </summary>
	private const string CityOfBrussels = "bruxelles";

	/// <summary>
	/// Label of the rule set that comes from the card's own sector.
	/// </summary>
	private const string SectorLabel = "your sector";
	#endregion

	#region Public Methods
	/// <summary>
	/// Selects the rule sets that apply to this street in a municipality for a card. The sector's rules apply only on the
	/// territory of the City of Brussels; a tolerance agreement applies only on its own municipality and only when the card
	/// carries its code.
	/// </summary>
	/// <param name="municipality">The municipality of the street surface, as parking.brussels writes it.</param>
	/// <param name="cardCodes">The tolerance codes the card carries.</param>
	/// <returns>The applicable rule sets; empty when nothing applies.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="cardCodes"/> is <see langword="null"/>.</exception>
	public IReadOnlyList<RuleSet> RuleSetsFor(string? municipality, IReadOnlySet<string> cardCodes)
	{
		ArgumentNullException.ThrowIfNull(cardCodes);
		var commune = NameNormalizer.Normalize(municipality);
		var sets = new List<RuleSet>();
		if (commune == CityOfBrussels && InSector)
		{
			sets.Add(new RuleSet(SectorLabel, Odd, Even));
		}

		foreach (var tolerance in Tolerances.Where(t => cardCodes.Contains(t.Code) && commune == NameNormalizer.Normalize(t.Commune)))
		{
			sets.Add(new RuleSet(tolerance.Label, tolerance.Odd, tolerance.Even));
		}

		return sets;
	}
	#endregion
}
