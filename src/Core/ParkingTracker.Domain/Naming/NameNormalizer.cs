using System.Text;
using System.Text.RegularExpressions;

namespace ParkingTracker.Domain.Naming;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Normalises street and municipality names so that the French and Dutch spellings written by the City lists,
///              parking.brussels and UrbIS compare equal (accents, case, hyphens and punctuation ignored).
/// </summary>
public static class NameNormalizer
{
	#region Static Instances
	/// <summary>
	/// Punctuation that separates words in a name: apostrophes, back quotes, hyphens and dots.
	/// </summary>
	private static readonly Regex Punctuation = new("[’'`\\-.]", RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);

	/// <summary>
	/// Runs of white space.
	/// </summary>
	private static readonly Regex Spaces = new("\\s+", RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);

	/// <summary>
	/// Separator between the French and Dutch halves of a bilingual name: " - " or a semicolon.
	/// </summary>
	private static readonly Regex PartSeparator = new("\\s+-\\s+|;", RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);

	/// <summary>
	/// The start of a word: the beginning of the text or a white space or hyphen, followed by its first character.
	/// </summary>
	private static readonly Regex WordStart = new("(^|[\\s-])\\S", RegexOptions.CultureInvariant, RegexSettings.MatchTimeout);
	#endregion

	#region Public Methods
	/// <summary>
	/// Lower-cases a name, removes its accents and turns apostrophes, hyphens and dots into single spaces.
	/// </summary>
	/// <param name="name">The name; <see langword="null"/> is read as empty.</param>
	/// <returns>The normalised name, for example <c>Rue de l'Étuve</c> becomes <c>rue de l etuve</c>.</returns>
	public static string Normalize(string? name)
	{
		var decomposed = (name ?? string.Empty).Normalize(NormalizationForm.FormD);
		var builder = new StringBuilder(decomposed.Length);
		foreach (var character in decomposed.Where(c => c is < '̀' or > 'ͯ'))
		{
			builder.Append(character);
		}

		var lower = builder.ToString().ToLowerInvariant();
		var spaced = Punctuation.Replace(lower, " ");
		return Spaces.Replace(spaced, " ").Trim();
	}

	/// <summary>
	/// Splits a bilingual name such as <c>Avenue Louise - Louizalaan</c> into its normalised, non-empty parts.
	/// </summary>
	/// <param name="name">The name; <see langword="null"/> is read as empty.</param>
	/// <returns>The normalised parts, in order; empty when the name has none.</returns>
	public static IReadOnlyList<string> NameParts(string? name)
	{
		return PartSeparator.Split(name ?? string.Empty)
			.Select(Normalize)
			.Where(part => part.Length > 0)
			.ToList();
	}

	/// <summary>
	/// Writes a name with a capital at the start of each word (after a space or a hyphen) and lower case elsewhere.
	/// </summary>
	/// <param name="name">The name; <see langword="null"/> is read as empty.</param>
	/// <returns>The name in title case, for example <c>SAINT-GILLES</c> becomes <c>Saint-Gilles</c>.</returns>
	public static string TitleCase(string? name)
	{
		return WordStart.Replace((name ?? string.Empty).ToLowerInvariant(), match => match.Value.ToUpperInvariant());
	}
	#endregion
}
