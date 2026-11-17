using System.Globalization;
using System.Text.Json;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Infrastructure.Tests.Features.SpotDecisions.Parity;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A recorded parity scenario: the inputs given to <c>live.js</c> (card, place, accuracy, clock) and the answer it
///              gave, read from <c>scenario.json</c>. The fixtures are in <c>test/fixtures/parity/&lt;name&gt;</c>.
/// </summary>
internal sealed class ParityScenario
{
	#region Constants
	/// <summary>
	/// The fixtures directory, relative to the repository root.
	/// </summary>
	private const string FixtureRoot = "test/fixtures/parity";
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParityScenario"/> class.
	/// </summary>
	/// <param name="name">Name of the scenario.</param>
	/// <param name="directory">Folder holding the scenario files.</param>
	/// <param name="root">Root JSON element of the scenario.</param>
	/// <param name="document">Parsed JSON document that owns the root element.</param>
	private ParityScenario(string name, string directory, JsonElement root, JsonDocument document)
	{
		Name = name;
		Directory = directory;
		Root = root;
		Document = document;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the scenario name, the name of its directory.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the directory with the scenario file and the recorded responses.
	/// </summary>
	public string Directory { get; }

	/// <summary>
	/// Gets the document that owns <see cref="Root"/>.
	/// </summary>
	public JsonDocument Document { get; }

	/// <summary>
	/// Gets the root of the scenario file.
	/// </summary>
	public JsonElement Root { get; }

	/// <summary>
	/// Gets the kind of the scenario: <c>decide</c>, <c>freeSpots</c> or <c>area</c>.
	/// </summary>
	public string Kind => Root.GetProperty("kind").GetString()!;

	/// <summary>
	/// Gets the moment the answer was recorded for.
	/// </summary>
	public DateTimeOffset Clock => DateTimeOffset.Parse(Root.GetProperty("clock").GetString()!, CultureInfo.InvariantCulture);

	/// <summary>
	/// Gets the place.
	/// </summary>
	public GeoPoint Point => new(Root.GetProperty("point").GetProperty("lat").GetDouble(), Root.GetProperty("point").GetProperty("lon").GetDouble());

	/// <summary>
	/// Gets the GPS accuracy, or <see langword="null"/>.
	/// </summary>
	public double? Accuracy => Root.GetProperty("accuracy") is { ValueKind: JsonValueKind.Number } accuracy ? accuracy.GetDouble() : null;

	/// <summary>
	/// Gets the answer <c>live.js</c> gave.
	/// </summary>
	public JsonElement Expected => Root.GetProperty("expected");

	/// <summary>
	/// Gets the outcome of the official-document check, or an undefined element when the card is not a City of Brussels card.
	/// </summary>
	public JsonElement OfficialList => Root.GetProperty("officialList");

	/// <summary>
	/// Gets the requests <c>live.js</c> made, in order and with repeats, as method and address.
	/// </summary>
	public IReadOnlyList<string> Requests => Root.GetProperty("requests").EnumerateArray()
		.Select(request => $"{request.GetProperty("method").GetString()} {request.GetProperty("url").GetString()}")
		.ToList();

	/// <summary>
	/// Gets the card.
	/// </summary>
	public ParkingCard Card
	{
		get
		{
			var card = Root.GetProperty("card");
			return new ParkingCard(
				card.GetProperty("municipality").GetString()!,
				card.GetProperty("sector").GetString(),
				new CardPeriod(Date(card, "from"), Date(card, "to")),
				Strings(card, "tolerances"),
				Strings(card, "mailles"),
				Strings(card, "lims"));
		}
	}
	#endregion

	#region Factory Methods
	/// <summary>
	/// Finds the recorded scenarios of a kind.
	/// </summary>
	/// <param name="kind">The kind: <c>decide</c>, <c>freeSpots</c> or <c>area</c>.</param>
	/// <returns>The scenario names.</returns>
	public static IEnumerable<object[]> NamesOfKind(string kind)
	{
		return All().Where(scenario => string.Equals(scenario.Kind, kind, StringComparison.Ordinal)).Select(scenario => new object[] { scenario.Name }).ToList();
	}

	/// <summary>
	/// Loads a recorded scenario.
	/// </summary>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>Scenario loaded from the recorded files.</returns>
	public static ParityScenario Load(string name)
	{
		var directory = Path.Combine(FixturesDirectory(), name);
		var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "scenario.json")));
		return new ParityScenario(name, directory, document.RootElement, document);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads every recorded scenario.
	/// </summary>
	/// <returns>Every recorded scenario.</returns>
	private static IEnumerable<ParityScenario> All()
	{
		return System.IO.Directory.EnumerateDirectories(FixturesDirectory()).Select(path => Load(Path.GetFileName(path)));
	}

	/// <summary>
	/// Looks for the fixtures directory above the test output.
	/// </summary>
	/// <returns>The path of the fixtures directory.</returns>
	private static string FixturesDirectory()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			var candidate = Path.Combine(directory.FullName, FixtureRoot);
			if (System.IO.Directory.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new DirectoryNotFoundException($"{FixtureRoot} was not found above {AppContext.BaseDirectory}.");
	}

	/// <summary>
	/// Reads an optional date of a card.
	/// </summary>
	/// <param name="card">JSON object of the saved card.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>The date, or <see langword="null"/> when it is empty.</returns>
	private static DateOnly? Date(JsonElement card, string name)
	{
		var text = card.GetProperty(name).GetString();
		return string.IsNullOrEmpty(text) ? null : DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Reads an array of texts of a card.
	/// </summary>
	/// <param name="card">JSON object of the saved card.</param>
	/// <param name="name">JSON property name to read.</param>
	/// <returns>Strings found under the property.</returns>
	private static List<string> Strings(JsonElement card, string name)
	{
		return card.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToList();
	}
	#endregion
}
