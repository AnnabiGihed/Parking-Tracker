using System.Globalization;
using System.Text.RegularExpressions;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;

namespace ParkingTracker.Application.Features.Cards.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the fields of a residents' card from the text recognised on its photo (ADR-0003). A port of
///              <c>parseCardText</c> of the first version, with the same patterns and heuristics: the card number (<c>B</c> or
///              <c>8</c>, then digits, with <c>O</c> read as <c>0</c>), the validity range <c>dd/MM/yyyy - dd/MM/yyyy</c>, the
///              plate, the municipality and City sector by name in French or Dutch, and the tolerance codes. A field that is not
///              found is left out, so that the form keeps what the user typed. The text is recognition output and may be noisy,
///              which is why every pattern has a match timeout.
/// </summary>
/// <remarks>
/// The municipality is found by the part of its name before the first dash or slash, as in the first version ("Saint" for
/// "Saint-Gilles"); the first municipality of the reference data that matches wins. A validity range with a date that does not
/// exist is ignored. Domain's <c>RegexSettings</c> is internal, so the same one-second timeout is declared here.
/// </remarks>
public static class CardTextParser
{
	#region Constants
	/// <summary>
	/// The identifier of the City of Brussels, which has City sectors and whose name is not looked for in the text.
	/// </summary>
	private const string CityId = "bruxelles";

	/// <summary>
	/// The format of the dates printed on the card.
	/// </summary>
	private const string CardDateFormat = "dd/MM/yyyy";

	/// <summary>
	/// The options shared by every pattern.
	/// </summary>
	private const RegexOptions Options = RegexOptions.CultureInvariant;
	#endregion

	#region Static Instances
	/// <summary>
	/// The longest a single match may take before it is abandoned with a timeout.
	/// </summary>
	private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

	/// <summary>
	/// The card number, for example <c>B000-123-456</c>; a leading <c>8</c> and letters <c>O</c> are misreadings.
	/// </summary>
	private static readonly Regex NumberPattern = new("\\b[B8][O0-9]{3}\\s*-\\s*[0-9]{3}\\s*-\\s*[0-9]{3}\\b", Options | RegexOptions.IgnoreCase, MatchTimeout);

	/// <summary>
	/// A leading <c>BO</c> of a card number, whose <c>O</c> is a zero.
	/// </summary>
	private static readonly Regex LeadingLetterO = new("^(?<letter>B)O", Options | RegexOptions.IgnoreCase, MatchTimeout);

	/// <summary>
	/// A letter <c>O</c> after <c>B</c> and a digit, which is a zero.
	/// </summary>
	private static readonly Regex LetterOAfterDigit = new("^(?<head>B[0-9])O", Options | RegexOptions.IgnoreCase, MatchTimeout);

	/// <summary>
	/// The validity range of the card.
	/// </summary>
	private static readonly Regex RangePattern = new("(?<from>[0-9]{2}/[0-9]{2}/[0-9]{4})\\s*-\\s*(?<to>[0-9]{2}/[0-9]{2}/[0-9]{4})", Options, MatchTimeout);

	/// <summary>
	/// A Belgian plate with a leading digit, for example <c>1ABC234</c>.
	/// </summary>
	private static readonly Regex NewPlatePattern = new("\\b[0-9][A-Z]{3}[0-9]{3}\\b", Options, MatchTimeout);

	/// <summary>
	/// A Belgian plate without a leading digit, for example <c>ABC234</c>.
	/// </summary>
	private static readonly Regex OldPlatePattern = new("\\b[A-Z]{3}[0-9]{3}\\b", Options, MatchTimeout);

	/// <summary>
	/// The separator that ends the part of a name that is looked for: a dash or a slash with its spaces.
	/// </summary>
	private static readonly Regex NameSeparator = new("\\s*[-/]\\s*", Options, MatchTimeout);

	/// <summary>
	/// Runs of white space, underscores, dashes and dots, which the text of a code may be broken by.
	/// </summary>
	private static readonly Regex CodeSeparators = new("[\\s_\\-.]+", Options, MatchTimeout);
	#endregion

	#region Public Methods
	/// <summary>
	/// Finds the card fields in the recognised text.
	/// </summary>
	/// <param name="text">The text recognised on the photo; may be empty or noisy.</param>
	/// <param name="data">The municipalities, City sectors and tolerance agreements to look for.</param>
	/// <returns>The fields that were found; all unset when none was.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> or <paramref name="data"/> is <see langword="null"/>.</exception>
	/// <exception cref="RegexMatchTimeoutException">Thrown when a pattern took longer than the timeout on this text.</exception>
	public static ParsedCard Parse(string text, ReferenceDataSet data)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(data);
		var normalised = text.Replace('\u2013', '-').Replace('\u2014', '-');
		var (from, to) = ReadPeriod(normalised);
		var municipality = FindMunicipality(normalised, data.Municipalities);
		var sector = FindSector(normalised, data.CitySectors);
		return new ParsedCard(
			ReadNumber(normalised),
			ReadPlate(normalised),
			from,
			to,
			municipality ?? (sector is null ? null : CityId),
			sector,
			FindTolerances(normalised, data.ToleranceSectors));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the card number and repairs the usual misreadings: spaces, a leading <c>8</c> for <c>B</c> and the letter <c>O</c> for zero.
	/// </summary>
	/// <param name="text">The normalised text.</param>
	/// <returns>The number without spaces, or <see langword="null"/>.</returns>
	private static string? ReadNumber(string text)
	{
		var match = NumberPattern.Match(text);
		if (!match.Success)
		{
			return null;
		}

		var number = new string([.. match.Value.Where(character => !char.IsWhiteSpace(character))]);
		if (number.StartsWith('8'))
		{
			number = "B" + number[1..];
		}

		number = LeadingLetterO.Replace(number, "${letter}0", 1);
		return LetterOAfterDigit.Replace(number, "${head}0", 1);
	}

	/// <summary>
	/// Reads the validity range.
	/// </summary>
	/// <param name="text">The normalised text.</param>
	/// <returns>The first and last day, or two <see langword="null"/> values when there is no range or a date does not exist.</returns>
	private static (DateOnly? From, DateOnly? To) ReadPeriod(string text)
	{
		var match = RangePattern.Match(text);
		if (match.Success
			&& DateOnly.TryParseExact(match.Groups["from"].Value, CardDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
			&& DateOnly.TryParseExact(match.Groups["to"].Value, CardDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
		{
			return (from, to);
		}

		return (null, null);
	}

	/// <summary>
	/// Reads the plate: the form with a leading digit first, then the one without.
	/// </summary>
	/// <param name="text">The normalised text.</param>
	/// <returns>The plate, or <see langword="null"/>.</returns>
	private static string? ReadPlate(string text)
	{
		var match = NewPlatePattern.Match(text);
		if (!match.Success)
		{
			match = OldPlatePattern.Match(text);
		}

		return match.Success ? match.Value : null;
	}

	/// <summary>
	/// Finds the first municipality, other than the City of Brussels, whose French or Dutch name is in the text.
	/// </summary>
	/// <param name="text">The normalised text.</param>
	/// <param name="municipalities">The municipalities in the order of the reference data.</param>
	/// <returns>The identifier of the municipality, or <see langword="null"/>.</returns>
	private static string? FindMunicipality(string text, IReadOnlyList<Municipality> municipalities)
	{
		return municipalities
			.Where(municipality => !string.Equals(municipality.Id, CityId, StringComparison.Ordinal))
			.FirstOrDefault(municipality => NameIsIn(text, municipality.Name, municipality.NameNl))?.Id;
	}

	/// <summary>
	/// Finds the first City of Brussels sector whose French or Dutch name is in the text.
	/// </summary>
	/// <param name="text">The normalised text.</param>
	/// <param name="sectors">The sectors in the order of the reference data.</param>
	/// <returns>The identifier of the sector, or <see langword="null"/>.</returns>
	private static string? FindSector(string text, IReadOnlyList<SectorDefinition> sectors)
	{
		return sectors.FirstOrDefault(sector => NameIsIn(text, sector.Name, sector.NameNl))?.Id;
	}

	/// <summary>
	/// Finds the tolerance codes in the text, ignoring case and the separators the code may be broken by.
	/// </summary>
	/// <param name="text">The normalised text.</param>
	/// <param name="sectors">The sectors that carry tolerance agreements.</param>
	/// <returns>The codes that are in the text, once each, in the order of the reference data.</returns>
	private static List<string> FindTolerances(string text, IReadOnlyList<SectorDefinition> sectors)
	{
		var loose = CodeSeparators.Replace(text.ToUpperInvariant(), "_");
		return sectors
			.SelectMany(sector => sector.Tolerances)
			.Select(tolerance => tolerance.Code)
			.Distinct(StringComparer.Ordinal)
			.Where(code => loose.Contains(code, StringComparison.Ordinal))
			.ToList();
	}

	/// <summary>
	/// Tells whether the French or the Dutch name appears in the text as whole words, ignoring case.
	/// </summary>
	/// <param name="text">The normalised text.</param>
	/// <param name="french">The French name.</param>
	/// <param name="dutch">The Dutch name.</param>
	/// <returns><see langword="true"/> when the first part of either name is in the text.</returns>
	private static bool NameIsIn(string text, string french, string dutch)
	{
		var words = new[] { FirstPart(french), FirstPart(dutch) }.Where(part => part.Length > 0).Select(Regex.Escape).ToArray();
		return words.Length > 0
			&& Regex.IsMatch(text, $"\\b({string.Join('|', words)})\\b", Options | RegexOptions.IgnoreCase, MatchTimeout);
	}

	/// <summary>
	/// Cuts a name before its first dash or slash.
	/// </summary>
	/// <param name="name">The display name.</param>
	/// <returns>The part before the first separator.</returns>
	private static string FirstPart(string name)
	{
		return NameSeparator.Split(name)[0];
	}
	#endregion
}
