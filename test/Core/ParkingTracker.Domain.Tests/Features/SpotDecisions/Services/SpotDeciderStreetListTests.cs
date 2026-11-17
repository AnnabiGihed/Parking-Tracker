using ParkingTracker.Domain.Features.Sectors.Services;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Domain.Tests.Features.Shared.TestDoubles;
using ParkingTracker.Domain.Tests.Features.SpotDecisions.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Tests.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the paths of <see cref="SpotDecider"/> that need more than a municipality: the street lists and
///              house numbers of the City of Brussels (check the signs, check the number, "free now" outside the paid hours)
///              and the border zones on a card.
/// </summary>
public sealed class SpotDeciderStreetListTests
{
	#region Constants
	/// <summary>
	/// The municipality whose official list the tests use.
	/// </summary>
	private const string CityMunicipality = "BRUXELLES";

	/// <summary>
	/// A Sunday, when paid parking does not apply.
	/// </summary>
	private static readonly DateTimeOffset Sunday = new(2026, 11, 15, 11, 0, 0, TimeSpan.FromHours(1));
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Runs the decider over the script and returns its decision.
	/// </summary>
	/// <param name="script">Script that answers the data requests of the decision.</param>
	/// <param name="question">The question being decided.</param>
	/// <returns>Decision.</returns>
	private static SpotDecision Decide(DecisionScript script, SpotQuestion question) => script.Run(SpotDecider.Begin(question));

	/// <summary>
	/// Creates a context for a street-list municipality with one street.
	/// </summary>
	/// <param name="odd">Cell text for the odd side of the street.</param>
	/// <param name="even">Cell text for the even side of the street.</param>
	/// <returns>Decision context.</returns>
	private static DecisionContext CityContext(string odd, string even)
	{
		var definition = new SectorDefinition("test", "Test", "Test", "bruxelles", null, [], [new SectorStreet("Rue Test", "Test straat", odd, even, null, true)], []);
		var sector = SectorModel.Build(definition).Value;
		return new DecisionContext(ZoneBuilder.Card(), ZoneBuilder.Municipality(MunicipalityKind.Streets, CityMunicipality), sector, null);
	}

	/// <summary>
	/// Creates a script that answers with one street-list zone.
	/// </summary>
	/// <returns>Decision script.</returns>
	private static DecisionScript CityScript()
	{
		var surface = ZoneBuilder.Surface("Rue Test", 4, municipality: CityMunicipality);
		return new DecisionScript
		{
			Zones = DecisionScript.Ok(surface),
			Rules = DecisionScript.RuleOf("TST1", RuleBuilder.Create(ZoneType.Derogation)),
			Spaces = Result.Success<IReadOnlyList<ReservedSpace>>([]),
			Works = Result.Success<IReadOnlyList<WorkItem>>([]),
		};
	}

	/// <summary>
	/// Creates an address point east of the standard position.
	/// </summary>
	/// <param name="number">House number of the address.</param>
	/// <param name="eastDegrees">Longitude offset east of the reference point in degrees.</param>
	/// <returns>Address point.</returns>
	private static AddressPoint Address(int number, double eastDegrees)
	{
		var position = new GeoPoint(ZoneBuilder.Here.Latitude, ZoneBuilder.Here.Longitude + eastDegrees);
		return new AddressPoint(number.ToString(System.Globalization.CultureInfo.InvariantCulture), new HouseNumber(number, string.Empty), WebMercator.ToMercator(position));
	}

	/// <summary>
	/// Wraps address points as a successful source answer.
	/// </summary>
	/// <param name="points">Address points the script returns.</param>
	/// <returns>The successful answer holding the points.</returns>
	private static Result<IReadOnlyList<AddressPoint>> Points(params AddressPoint[] points) => Result.Success<IReadOnlyList<AddressPoint>>(points);
	#endregion

	#region Street List Tests
	/// <summary>
	/// Verifies that a street that is in the sector only under a condition on both sides gives "check the signs".
	/// </summary>
	[Fact]
	public void Decide_StreetWithConditionOnBothSides_ShouldCheckTheSigns()
	{
		var decision = Decide(CityScript(), ZoneBuilder.Question(CityContext("check: côté parc", "check: côté parc")));

		decision.Verdict.Should().Be(Verdict.CheckSigns);
		decision.Title.Should().Be("CHECK THE SIGNS");
		decision.Detail.Should().Contain("is in your sector only under a condition");
	}

	/// <summary>
	/// Verifies that "check the signs" becomes "free now" outside the paid hours and keeps the earlier answer as a note.
	/// </summary>
	[Fact]
	public void Decide_CheckTheSignsOnSunday_ShouldBeFreeNow()
	{
		var decision = Decide(CityScript(), ZoneBuilder.Question(CityContext("check: côté parc", "check: côté parc"), now: Sunday));

		decision.Verdict.Should().Be(Verdict.FreeNow);
		decision.Notes.Should().ContainSingle().Which.Should().StartWith("During paid hours: Rue Test is in your sector only under a condition");
	}

	/// <summary>
	/// Verifies that a place between a covered and an uncovered house number gives "check the number" and names both.
	/// </summary>
	[Fact]
	public void Decide_AtTheLimitBetweenNumbers_ShouldCheckTheNumber()
	{
		var script = CityScript();
		script.Addresses = Points(Address(4, 0.00002), Address(6, 0.00003));

		var decision = Decide(script, ZoneBuilder.Question(CityContext("1-9", "2-4")));

		decision.Verdict.Should().Be(Verdict.CheckNumber);
		decision.Title.Should().Be("CHECK THE NUMBER");
		decision.Detail.Should().Be("You're at a limit on Rue Test: free in front of 4, pay in front of 6.");
	}

	/// <summary>
	/// Verifies that "check the number" becomes "free now" outside the paid hours.
	/// </summary>
	[Fact]
	public void Decide_CheckTheNumberOnSunday_ShouldBeFreeNow()
	{
		var script = CityScript();
		script.Addresses = Points(Address(4, 0.00002), Address(6, 0.00003));

		var decision = Decide(script, ZoneBuilder.Question(CityContext("1-9", "2-4"), now: Sunday));

		decision.Verdict.Should().Be(Verdict.FreeNow);
		decision.Title.Should().Be("FREE NOW");
		decision.Notes.Should().ContainSingle().Which.Should().StartWith("During paid hours: You're at a limit on Rue Test");
	}

	/// <summary>
	/// Verifies that failed house numbers give "can't verify" with the reason and the rules of the street.
	/// </summary>
	[Fact]
	public void Decide_AddressesFail_ShouldCantVerifyWithTheReason()
	{
		var script = CityScript();
		script.Addresses = DecisionScript.Failed<IReadOnlyList<AddressPoint>>("urbis down");

		var decision = Decide(script, ZoneBuilder.Question(CityContext("1-9", "2-4")));

		decision.Verdict.Should().Be(Verdict.CantVerify);
		decision.Detail.Should().StartWith("Could not get the official house numbers (urbis down).");
	}
	#endregion

	#region Border Zone Tests
	/// <summary>
	/// Verifies that standing inside a border zone of the card is free, whatever the municipality of the surface.
	/// </summary>
	[Fact]
	public void Decide_InsideABorderZoneOfTheCard_ShouldBeFree()
	{
		var script = new DecisionScript
		{
			Zones = DecisionScript.Ok(ZoneBuilder.Surface("Rue Test", 4, municipality: "UCCLE")),
			Rules = DecisionScript.RuleOf("TST1", RuleBuilder.Create(ZoneType.Derogation)),
			Mailles = Result.Success<IReadOnlyList<SectorMaille>>([new SectorMaille("LIM1", "Limite Test", ZoneBuilder.Surface("Limite", 4).Shape)]),
			Spaces = Result.Success<IReadOnlyList<ReservedSpace>>([]),
			Works = Result.Success<IReadOnlyList<WorkItem>>([]),
		};
		var context = ZoneBuilder.Context(MunicipalityKind.Whole, ZoneBuilder.Card(lims: ["LIM1"]));

		var decision = Decide(script, ZoneBuilder.Question(context));

		decision.Verdict.Should().Be(Verdict.Free);
		decision.Detail.Should().Be("Rue Test: border zone Limite Test (on your card).");
		script.Asked.Should().Contain("mailles");
	}
	#endregion
}
