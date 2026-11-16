using System.Text.RegularExpressions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Addressing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Which house numbers of one side of a street a rule covers, parsed from the text of the official list.
/// </summary>
/// <param name="Kind">How the side is treated.</param>
/// <param name="Ranges">The covered ranges; empty unless <paramref name="Kind"/> is <see cref="SideRuleKind.Ranges"/>.</param>
/// <param name="Text">The rule as shown to the user.</param>
public sealed record SideRule(SideRuleKind Kind, IReadOnlyList<HouseRange> Ranges, string Text)
{
	#region Constants
	/// <summary>
	/// Failure code for a range or "to the end" part that does not start with a house number.
	/// </summary>
	public const string InvalidRangeCode = "SideRule.InvalidRange";

	/// <summary>
	/// Text of a side that is not covered.
	/// </summary>
	private const string NotCoveredText = "not covered";

	/// <summary>
	/// Prefix that introduces a condition in words.
	/// </summary>
	private const string ConditionPrefix = "check:";
	#endregion

	#region Static Instances
	/// <summary>
	/// Separator between the parts of a list: " et " or a comma.
	/// </summary>
	private static readonly Regex PartSeparator = new("\\s+et\\s+|,", RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);

	/// <summary>
	/// A part such as <c>59 à fin</c>: from a number to the end of the street.
	/// </summary>
	private static readonly Regex ToEnd = new("^(\\S+)\\s*(?:à|a)\\s*fin", RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);

	/// <summary>
	/// A part such as <c>17-57b</c>: from one number to another.
	/// </summary>
	private static readonly Regex Between = new("^(\\S+?)\\s*-\\s*(\\S+)$", RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);
	#endregion

	#region Properties
	/// <summary>
	/// Gets a value indicating whether the whole side is covered.
	/// </summary>
	public bool IsAll => Kind == SideRuleKind.All;

	/// <summary>
	/// Gets a value indicating whether the side is covered only under a condition in words.
	/// </summary>
	public bool IsCondition => Kind == SideRuleKind.Condition;
	#endregion

	#region Factory Methods
	/// <summary>
	/// Parses the text of one side of a street. <c>hors</c> means not covered, <c>check: text</c> a condition in words,
	/// otherwise a list of numbers and ranges (<c>17-57b</c>, <c>59 à fin</c>, <c>2-6 et 64-76</c>, <c>7 et 8</c>, <c>1,3</c>).
	/// </summary>
	/// <param name="raw">The text as printed in the list; <see langword="null"/> is read as empty.</param>
	/// <param name="emptyCell">How to read an empty text, which depends on the list the row comes from.</param>
	/// <returns>The rule, or a <see cref="InvalidRangeCode"/> failure when a range does not start with a house number.</returns>
	public static Result<SideRule> Parse(string? raw, EmptyCellMeaning emptyCell)
	{
		var text = emptyCell == EmptyCellMeaning.NotCovered && string.IsNullOrEmpty(raw) ? "hors" : raw ?? string.Empty;
		var lower = text.Trim().ToLowerInvariant();
		var special = ParseSpecialCell(text, lower);
		return special is not null ? Result.Success(special) : ParseRanges(text, lower);
	}

	/// <summary>
	/// Creates the rule of a side that is not covered.
	/// </summary>
	/// <returns>A rule of kind <see cref="SideRuleKind.None"/>.</returns>
	public static SideRule NotCovered() => new(SideRuleKind.None, [], NotCoveredText);
	#endregion

	#region Public Methods
	/// <summary>
	/// Determines whether the rule covers a house number. A condition in words never covers a number by itself.
	/// </summary>
	/// <param name="house">The house number.</param>
	/// <returns><see langword="true"/> when the whole side is covered or a listed range contains the number.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="house"/> is <see langword="null"/>.</exception>
	public bool Covers(HouseNumber house)
	{
		ArgumentNullException.ThrowIfNull(house);
		return Kind switch
		{
			SideRuleKind.All => true,
			SideRuleKind.Ranges => Ranges.Any(range => range.Contains(house)),
			_ => false,
		};
	}
	#endregion

	#region Private Helpers
	private static SideRule? ParseSpecialCell(string text, string lower)
	{
		if (lower.Length == 0)
		{
			return new SideRule(SideRuleKind.All, [], "whole side");
		}

		if (lower.StartsWith("hors", StringComparison.Ordinal))
		{
			return NotCovered();
		}

		return lower.StartsWith(ConditionPrefix, StringComparison.Ordinal)
			? new SideRule(SideRuleKind.Condition, [], text[ConditionPrefix.Length..].Trim())
			: null;
	}

	private static Result<SideRule> ParseRanges(string text, string lower)
	{
		var ranges = new List<HouseRange>();
		foreach (var part in PartSeparator.Split(lower).Select(p => p.Trim()))
		{
			var parsed = ParsePart(part);
			if (parsed.IsInvalid)
			{
				return Result.Failure<SideRule>(new Error(InvalidRangeCode, $"'{text}' is not a valid house-number rule: '{part}' does not start with a house number."));
			}

			if (parsed.Range is not null)
			{
				ranges.Add(parsed.Range);
			}
		}

		return Result.Success(new SideRule(SideRuleKind.Ranges, ranges, text.Replace("à fin", "→ end", StringComparison.Ordinal)));
	}

	private static PartParse ParsePart(string part)
	{
		var toEnd = ToEnd.Match(part);
		if (toEnd.Success)
		{
			var first = HouseNumber.Parse(toEnd.Groups[1].Value);
			return first is null ? PartParse.Invalid : PartParse.Of(new HouseRange(first.LowKey, long.MaxValue));
		}

		var between = Between.Match(part);
		if (between.Success)
		{
			var from = HouseNumber.Parse(between.Groups[1].Value);
			var to = HouseNumber.Parse(between.Groups[2].Value);
			return from is null || to is null ? PartParse.Invalid : PartParse.Of(new HouseRange(from.LowKey, to.HighKey));
		}

		var single = HouseNumber.Parse(part);
		return single is null ? PartParse.Skipped : PartParse.Of(new HouseRange(single.LowKey, single.HighKey));
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The outcome of reading one part of a list: a range, a part that is skipped, or an invalid part.
	/// </summary>
	/// <param name="Range">The range read, or <see langword="null"/> when the part is skipped or invalid.</param>
	/// <param name="IsInvalid">Whether the part is a range that does not start with a house number.</param>
	private readonly record struct PartParse(HouseRange? Range, bool IsInvalid)
	{
		#region Static Instances
		/// <summary>
		/// A part that is not a number at all and is ignored.
		/// </summary>
		public static readonly PartParse Skipped = new(null, false);

		/// <summary>
		/// A range or "to the end" part that does not start with a house number.
		/// </summary>
		public static readonly PartParse Invalid = new(null, true);
		#endregion

		#region Factory Methods
		/// <summary>
		/// Creates the outcome of a part that was read as a range.
		/// </summary>
		/// <param name="range">The range.</param>
		/// <returns>A valid outcome holding the range.</returns>
		public static PartParse Of(HouseRange range) => new(range, false);
		#endregion
	}
	#endregion
}
