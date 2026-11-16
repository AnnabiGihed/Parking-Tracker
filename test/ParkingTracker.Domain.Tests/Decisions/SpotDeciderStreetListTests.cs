using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.ReferenceData;
using ParkingTracker.Domain.Tests.TestDoubles;
using ParkingTracker.Domain.Zones;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Tests.Decisions;

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
	private const string CityMunicipality = "BRUXELLES";
	private static readonly DateTimeOffset Sunday = new(2026, 11, 15, 11, 0, 0, TimeSpan.FromHours(1));
	#endregion

	#region Test Infrastructure
	private static SpotDecision Decide(DecisionScript script, SpotQuestion question) => script.Run(SpotDecider.Begin(question));

	private static DecisionContext CityContext(string odd, string even)
	{
		var definition = new SectorDefinition("test", "Test", "Test", "bruxelles", null, [], [new SectorStreet("Rue Test", "Test straat", odd, even, null, true)], []);
		var sector = SectorModel.Build(definition).Value;
		return new DecisionContext(ZoneBuilder.Card(), ZoneBuilder.Municipality(MunicipalityKind.Streets, CityMunicipality), sector, null);
	}

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

	private static AddressPoint Address(int number, double eastDegrees)
	{
		var position = new GeoPoint(ZoneBuilder.Here.Latitude, ZoneBuilder.Here.Longitude + eastDegrees);
		return new AddressPoint(number.ToString(System.Globalization.CultureInfo.InvariantCulture), new HouseNumber(number, string.Empty), WebMercator.ToMercator(position));
	}

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
