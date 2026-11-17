using System.Globalization;
using System.Text.RegularExpressions;
using ParkingTracker.Domain.Features.Shared.Services;

namespace ParkingTracker.Domain.Features.Sectors.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A house number with an optional letter suffix (<c>57b</c>), and the ordering keys the City lists
///              use to compare numbers: <c>74</c> covers <c>74A</c> and <c>74B</c>.
/// </summary>
/// <param name="Number">The numeric part.</param>
/// <param name="Letter">The lower-case letter suffix, or an empty string when there is none.</param>
public sealed record HouseNumber(int Number, string Letter)
{
	#region Constants
	/// <summary>
	/// Rank given to "no letter" when the number is the upper end of a range, so that <c>74</c> includes every suffix.
	/// </summary>
	private const int NoLetterHighRank = 99;
	#endregion

	#region Static Instances
	/// <summary>
	/// A number followed by an optional letter, ignoring anything after it.
	/// </summary>
	private static readonly Regex Pattern = new("^(?<number>[0-9]+)\\s*(?<letter>[a-zA-Z]?)", RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);
	#endregion

	#region Properties
	/// <summary>
	/// Gets a value indicating whether the number is on the odd side of the street.
	/// </summary>
	public bool IsOdd => Number % 2 != 0;

	/// <summary>
	/// Gets the side of the street the number is on.
	/// </summary>
	public StreetSide Side => IsOdd ? StreetSide.Odd : StreetSide.Even;

	/// <summary>
	/// Gets the key of this number as the lower end of a range: the number times 100 plus the letter rank (a = 1, none = 0).
	/// </summary>
	public long LowKey => (Number * 100L) + LetterRank;

	/// <summary>
	/// Gets the key of this number as the upper end of a range: like <see cref="LowKey"/> but 99 when there is no letter.
	/// </summary>
	public long HighKey => (Number * 100L) + (Letter.Length > 0 ? LetterRank : NoLetterHighRank);

	/// <summary>
	/// Gets the position of the letter in the alphabet (a = 1), or 0 when there is none.
	/// </summary>
	private int LetterRank => Letter.Length > 0 ? Letter[0] - 'a' + 1 : 0;
	#endregion

	#region Factory Methods
	/// <summary>
	/// Reads a house number from the start of a text such as <c>57b</c>, <c>74 A</c> or <c>12</c>.
	/// </summary>
	/// <param name="text">The text; <see langword="null"/> is read as empty.</param>
	/// <returns>The house number, or <see langword="null"/> when the text does not start with a number that fits an integer.</returns>
	public static HouseNumber? Parse(string? text)
	{
		var match = Pattern.Match((text ?? string.Empty).Trim());
		if (!match.Success || !int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
		{
			return null;
		}

		return new HouseNumber(number, match.Groups["letter"].Value.ToLowerInvariant());
	}
	#endregion
}
