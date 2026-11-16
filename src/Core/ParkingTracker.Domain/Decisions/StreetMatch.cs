using ParkingTracker.Domain.Addressing;

namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The street of the card's sector that matches the surface, with the rule sets that apply in its municipality.
/// </summary>
/// <param name="Street">The street of the sector.</param>
/// <param name="Sets">The rule sets that apply to the card; never empty when house numbers are looked at.</param>
/// <param name="RuleText">The rule sets in plain text, for the answer.</param>
internal sealed record StreetMatch(StreetRules Street, IReadOnlyList<RuleSet> Sets, string RuleText);
