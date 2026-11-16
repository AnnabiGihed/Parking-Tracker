using ParkingTracker.Domain.Naming;
using ParkingTracker.Domain.ReferenceData;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Addressing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A sector definition turned into parsed street rules, with the lookup of a street by its normalised
///              French or Dutch name.
/// </summary>
public sealed class SectorModel
{
	#region Constants
	/// <summary>
	/// Failure code for a street whose rule text cannot be parsed.
	/// </summary>
	public const string InvalidRuleCode = "Sector.InvalidRule";
	#endregion

	#region Fields
	private readonly Dictionary<string, int> lookup;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SectorModel"/> class from streets that are already parsed.
	/// </summary>
	/// <param name="definition">The definition the model is built from.</param>
	/// <param name="streets">The parsed streets, in first-seen order.</param>
	/// <param name="lookup">The index in <paramref name="streets"/> of every normalised French and Dutch name.</param>
	private SectorModel(SectorDefinition definition, IReadOnlyList<StreetRules> streets, Dictionary<string, int> lookup)
	{
		Definition = definition;
		Streets = streets;
		this.lookup = lookup;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the definition the model was built from.
	/// </summary>
	public SectorDefinition Definition { get; }

	/// <summary>
	/// Gets the streets of the sector followed by the streets only a tolerance agreement lists, in first-seen order.
	/// </summary>
	public IReadOnlyList<StreetRules> Streets { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Parses the rules of every street of a sector and its tolerance agreements. A row is read with its own
	/// <see cref="SectorStreet.EmptyMeansWholeSide"/>; a street listed again (by French name) is the same street.
	/// </summary>
	/// <param name="definition">The sector definition.</param>
	/// <returns>The model, or a <see cref="InvalidRuleCode"/> failure naming the street whose rule cannot be parsed.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
	public static Result<SectorModel> Build(SectorDefinition definition)
	{
		ArgumentNullException.ThrowIfNull(definition);
		var builders = new List<StreetBuilder>();
		var lookup = new Dictionary<string, int>();
		foreach (var (row, tolerance) in RowsOf(definition))
		{
			var rules = ParseRow(definition, row);
			if (rules.IsFailure)
			{
				return Result.Failure<SectorModel>(rules.Error);
			}

			var street = GetOrAdd(builders, lookup, row);
			if (tolerance is null)
			{
				(street.Odd, street.Even) = rules.Value;
				street.InSector = true;
			}
			else
			{
				street.Tolerances.Add(new ToleranceRule(tolerance.Code, tolerance.Label, tolerance.Commune, rules.Value.Odd, rules.Value.Even));
			}
		}

		var streets = builders.Select(b => new StreetRules(b.Index, b.FrenchName, b.DutchName, b.Odd, b.Even, b.InSector, b.Tolerances)).ToList();
		return Result.Success(new SectorModel(definition, streets, lookup));
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Finds a street by name. Each name is split on <c> - </c> and <c>;</c> (so <c>Avenue Louise - Louizalaan</c> matches
	/// either language) and compared normalised; the first name part that matches wins.
	/// </summary>
	/// <param name="names">The names to try in order, for example the French then the Dutch name of a street surface.</param>
	/// <returns>The street, or <see langword="null"/> when no name matches.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="names"/> is <see langword="null"/>.</exception>
	public StreetRules? FindStreet(params string?[] names)
	{
		ArgumentNullException.ThrowIfNull(names);
		foreach (var name in names)
		{
			foreach (var part in NameNormalizer.NameParts(name))
			{
				if (lookup.TryGetValue(part, out var index))
				{
					return Streets[index];
				}
			}
		}

		return null;
	}
	#endregion

	#region Private Helpers
	private static IEnumerable<(SectorStreet Row, ToleranceAgreement? Tolerance)> RowsOf(SectorDefinition definition)
	{
		var sectorRows = definition.Streets.Select(row => (row, (ToleranceAgreement?)null));
		var toleranceRows = definition.Tolerances.SelectMany(tolerance => tolerance.Streets.Select(row => (row, (ToleranceAgreement?)tolerance)));
		return sectorRows.Concat(toleranceRows);
	}

	private static Result<(SideRule Odd, SideRule Even)> ParseRow(SectorDefinition definition, SectorStreet row)
	{
		var meaning = row.EmptyMeansWholeSide ? EmptyCellMeaning.WholeSide : EmptyCellMeaning.NotCovered;
		var odd = SideRule.Parse(row.Odd, meaning);
		var even = odd.IsSuccess ? SideRule.Parse(row.Even, meaning) : odd;
		if (odd.IsFailure || even.IsFailure)
		{
			var cause = odd.IsFailure ? odd.Error : even.Error;
			return Result.Failure<(SideRule, SideRule)>(new Error(InvalidRuleCode, $"Sector '{definition.Id}', street '{row.FrenchName}': {cause.Message}"));
		}

		return Result.Success((odd.Value, even.Value));
	}

	private static StreetBuilder GetOrAdd(List<StreetBuilder> builders, Dictionary<string, int> lookup, SectorStreet row)
	{
		var key = NameNormalizer.Normalize(row.FrenchName);
		if (lookup.TryGetValue(key, out var existing))
		{
			return builders[existing];
		}

		var street = new StreetBuilder(builders.Count, row.FrenchName, row.DutchName);
		builders.Add(street);
		lookup[key] = street.Index;
		lookup[NameNormalizer.Normalize(row.DutchName)] = street.Index;
		return street;
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Mutable street under construction while a sector is built.
	/// </summary>
	private sealed class StreetBuilder
	{
		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="StreetBuilder"/> class that is in no sector and has no tolerance.
		/// </summary>
		/// <param name="index">Position of the street.</param>
		/// <param name="frenchName">Street name in French.</param>
		/// <param name="dutchName">Street name in Dutch.</param>
		public StreetBuilder(int index, string frenchName, string dutchName)
		{
			Index = index;
			FrenchName = frenchName;
			DutchName = dutchName;
		}
		#endregion

		#region Properties
		/// <summary>
		/// Gets the position of the street.
		/// </summary>
		public int Index { get; }

		/// <summary>
		/// Gets the street name in French.
		/// </summary>
		public string FrenchName { get; }

		/// <summary>
		/// Gets the street name in Dutch.
		/// </summary>
		public string DutchName { get; }

		/// <summary>
		/// Gets or sets the sector rule of the odd side.
		/// </summary>
		public SideRule Odd { get; set; } = SideRule.NotCovered();

		/// <summary>
		/// Gets or sets the sector rule of the even side.
		/// </summary>
		public SideRule Even { get; set; } = SideRule.NotCovered();

		/// <summary>
		/// Gets or sets a value indicating whether the sector's own list contains the street.
		/// </summary>
		public bool InSector { get; set; }

		/// <summary>
		/// Gets the tolerance rules collected for the street.
		/// </summary>
		public List<ToleranceRule> Tolerances { get; } = [];
		#endregion
	}
	#endregion
}
