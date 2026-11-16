using System.Text.RegularExpressions;

namespace ParkingTracker.Domain.Pricing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Decides what kind of zone a street surface is for a residents' card, from its parking.brussels colour code
///              and its live rule, and detects event zones.
/// </summary>
public static class ZoneClassifier
{
	#region Constants
	/// <summary>
	/// Colour code of an unregulated zone.
	/// </summary>
	private const int UnregulatedColour = 0;

	/// <summary>
	/// Colour code of a red (payable) zone.
	/// </summary>
	private const int RedColour = 1;

	/// <summary>
	/// Colour code of a grey zone, a derogation zone.
	/// </summary>
	private const int GreyColour = 3;

	/// <summary>
	/// Colour code of a green zone, a derogation zone.
	/// </summary>
	private const int GreenColour = 4;

	/// <summary>
	/// Colour code of a blue (parking disc) zone.
	/// </summary>
	private const int BlueColour = 5;

	/// <summary>
	/// Colour code of an event zone.
	/// </summary>
	private const int EventColour = 6;

	/// <summary>
	/// Colour code of residents-only spaces.
	/// </summary>
	private const int ResidentsColour = 9;
	#endregion

	#region Static Instances
	/// <summary>
	/// The word "événement" with or without the accent, in a rule name.
	/// </summary>
	private static readonly Regex EventName = new("v[ée]nement", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);
	#endregion

	#region Public Methods
	/// <summary>
	/// Classifies a zone. A colour code 0 is free; otherwise the live rule's type wins (reserved spaces with colour 9 are
	/// residents-only); with no rule only the colours that mean the same everywhere are used: red is payable, grey and
	/// green are derogation, blue is a disc zone, colour 9 is residents-only. Orange depends on the municipality.
	/// </summary>
	/// <param name="colourCode">The <c>typreg</c> colour code of the surface, or <see langword="null"/> when missing.</param>
	/// <param name="rule">The live rule of the zone, or <see langword="null"/> when unknown.</param>
	/// <returns>The zone type, or <see langword="null"/> when it cannot be told (for example orange without a rule).</returns>
	public static ZoneType? Classify(int? colourCode, ParkingRule? rule)
	{
		if (colourCode == UnregulatedColour)
		{
			return ZoneType.Free;
		}

		if (rule?.Type is { } ruleType)
		{
			return ruleType == ZoneType.Reserved && colourCode == ResidentsColour ? ZoneType.Residents : ruleType;
		}

		return colourCode switch
		{
			RedColour => ZoneType.Payable,
			GreyColour or GreenColour => ZoneType.Derogation,
			BlueColour => ZoneType.Disk,
			ResidentsColour => ZoneType.Residents,
			_ => null,
		};
	}

	/// <summary>
	/// Determines whether a zone is an event zone, where the normal rules do not apply.
	/// </summary>
	/// <param name="colourCode">The <c>typreg</c> colour code of the surface, or <see langword="null"/>.</param>
	/// <param name="rule">The live rule of the zone, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> for colour code 6 or a rule whose name contains "événement".</returns>
	public static bool IsEvent(int? colourCode, ParkingRule? rule)
	{
		return colourCode == EventColour || EventName.IsMatch(rule?.Name ?? string.Empty);
	}
	#endregion
}
