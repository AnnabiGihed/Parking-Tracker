using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.PaidHours;

namespace ParkingTracker.Infrastructure.Tests.Features.PaidHours;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ParkingRuleParser"/>: the fields of a rule (numbers that arrive as text included), which
///              rules are in force at a moment and which one wins when a zone has several.
/// </summary>
public sealed class ParkingRuleParserTests
{
	#region Test Infrastructure
	/// <summary>
	/// The moment the tests run at.
	/// </summary>
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 11, 0, 0, TimeSpan.FromHours(1));

	/// <summary>
	/// Creates the JSON of one derogation rule.
	/// </summary>
	/// <param name="code">Zone code of the rule.</param>
	/// <param name="extra">Extra JSON properties appended to the rule.</param>
	/// <returns>Rule JSON.</returns>
	private static string Rule(string code, string extra = "") => $"{{\"geometry\":null,\"properties\":{{\"code\":\"{code}\",\"type\":\"DEROGATION\",\"mo\":1,\"tu\":1,\"sa\":1,\"so\":0,\"special_day\":0{extra}}}}}";

	/// <summary>
	/// Wraps rule JSON texts in a feature collection.
	/// </summary>
	/// <param name="rules">Rule JSON texts to put in the collection.</param>
	/// <returns>Collection JSON.</returns>
	private static string Collection(params string[] rules) => $"{{\"features\":[{string.Join(",", rules)}]}}";
	#endregion

	#region Field Tests
	/// <summary>
	/// Verifies that every field of a rule is read, numbers given as text included.
	/// </summary>
	[Fact]
	public void Parse_Rule_ShouldReadEveryField()
	{
		var json = Collection(Rule("BRU4", ",\"name\":\"Bruxelles - Zone grise\",\"start_hour\":\"09:00\",\"end_hour\":\"21:00\",\"charge60\":3.6,\"free_time\":\"20\",\"max_time\":\"270\",\"fee\":\"47\""));

		var rule = ParkingRuleParser.Parse(json, Now).Value["BRU4"];

		rule.Type.Should().Be(ZoneType.Derogation);
		rule.Name.Should().Be("Bruxelles - Zone grise");
		rule.PaidDays.Should().BeEquivalentTo([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Saturday]);
		rule.PaidOnHolidays.Should().BeFalse();
		(rule.StartHour, rule.EndHour).Should().Be(("09:00", "21:00"));
		(rule.Charge60, rule.FreeTime, rule.MaxTime, rule.Fee).Should().Be((3.6m, 20, 270, 47m));
	}

	/// <summary>
	/// Verifies that a zone type the app does not know is read as unknown, not guessed.
	/// </summary>
	[Fact]
	public void Parse_UnknownType_ShouldBeNull()
	{
		var json = "{\"features\":[{\"properties\":{\"code\":\"X1\",\"type\":\"MYSTERY\"}}]}";

		ParkingRuleParser.Parse(json, Now).Value["X1"].Type.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a rule that also applies on public holidays says so.
	/// </summary>
	[Fact]
	public void Parse_SpecialDay_ShouldMeanPaidOnHolidays()
	{
		var json = Collection(Rule("X1").Replace("\"special_day\":0", "\"special_day\":1", StringComparison.Ordinal));

		ParkingRuleParser.Parse(json, Now).Value["X1"].PaidOnHolidays.Should().BeTrue();
	}
	#endregion

	#region In Force Tests
	/// <summary>
	/// Verifies that inactive, disabled and not yet active rules, and rules without a code, are left out.
	/// </summary>
	[Fact]
	public void Parse_NotInForce_ShouldBeLeftOut()
	{
		var json = Collection(
			Rule("OK1"),
			Rule("OFF", ",\"active\":0"),
			Rule("DISABLED", ",\"disabled_on\":\"2026-11-01T00:00:00\""),
			Rule("FUTURE", ",\"active_from\":\"2026-12-01T00:00:00\""),
			"{\"properties\":{\"type\":\"DISK\"}}");

		ParkingRuleParser.Parse(json, Now).Value.Keys.Should().Equal("OK1");
	}

	/// <summary>
	/// Verifies that a rule that gets disabled later is still in force now.
	/// </summary>
	[Fact]
	public void Parse_DisabledInTheFuture_ShouldStayInForce()
	{
		var json = Collection(Rule("SOON", ",\"disabled_on\":\"2027-01-01T00:00:00\""));

		ParkingRuleParser.Parse(json, Now).Value.Should().ContainKey("SOON");
	}

	/// <summary>
	/// Verifies that when a zone has several rules in force, the one that started last wins.
	/// </summary>
	[Fact]
	public void Parse_SeveralRulesForAZone_ShouldKeepTheLatestStart()
	{
		var json = Collection(
			Rule("Z1", ",\"active_from\":\"2025-07-01T00:00:00\",\"name\":\"old\""),
			Rule("Z1", ",\"active_from\":\"2026-03-01T00:00:00\",\"name\":\"new\""),
			Rule("Z1", ",\"active_from\":\"2024-01-01T00:00:00\",\"name\":\"older\""));

		ParkingRuleParser.Parse(json, Now).Value["Z1"].Name.Should().Be("new");
	}

	/// <summary>
	/// Verifies that an answer that is not a feature collection is a bad-response failure.
	/// </summary>
	[Fact]
	public void Parse_UnexpectedAnswer_ShouldFail()
	{
		var result = ParkingRuleParser.Parse("{\"detail\":\"nope\"}", Now);

		result.Error.Code.Should().Be("ParkingRules.BadResponse");
		result.Error.Message.Should().Be("parking rules: unexpected answer");
	}
	#endregion
}
