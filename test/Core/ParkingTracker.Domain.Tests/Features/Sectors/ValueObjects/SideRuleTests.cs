using ParkingTracker.Domain.Features.Sectors.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.Sectors.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SideRule"/> and <see cref="RuleSet"/>: parsing the official list texts and
///              covering house numbers exactly as <c>parseRule</c>, <c>ruleCovers</c>, <c>setsCover</c> and <c>sideCheck</c> of live.js do.
/// </summary>
public sealed class SideRuleTests
{
	#region Test Infrastructure
	/// <summary>
	/// Parses a cell and fails the test when the cell is invalid.
	/// </summary>
	/// <param name="raw">Raw cell text; null when the cell is empty.</param>
	/// <param name="emptyCell">How an empty cell is read.</param>
	/// <returns>Parsed side rule.</returns>
	private static SideRule Parse(string? raw, EmptyCellMeaning emptyCell = EmptyCellMeaning.WholeSide)
	{
		var result = SideRule.Parse(raw, emptyCell);
		result.IsSuccess.Should().BeTrue();
		return result.Value;
	}

	/// <summary>
	/// Parses a house number and fails the test when the text is invalid.
	/// </summary>
	/// <param name="text">The text to read.</param>
	/// <returns>Parsed house number.</returns>
	private static HouseNumber House(string text) => HouseNumber.Parse(text)!;

	/// <summary>
	/// Lists the numbers of the sequence that the rule covers.
	/// </summary>
	/// <param name="rule">Side rule whose covered numbers are listed.</param>
	/// <param name="numbers">Space-separated house numbers to test.</param>
	/// <returns>Covered numbers, in order.</returns>
	private static IEnumerable<string> Covered(SideRule rule, string numbers) => numbers.Split(' ').Where(n => rule.Covers(House(n)));
	#endregion

	#region Whole Side And Not Covered Tests
	/// <summary>
	/// Verifies that an empty cell of a sector's street list covers the whole side.
	/// </summary>
	/// <param name="raw">The cell text.</param>
	[Theory]
	[InlineData("")]
	[InlineData(null)]
	public void Parse_EmptyInSectorList_ShouldCoverTheWholeSide(string? raw)
	{
		var rule = Parse(raw);

		rule.Kind.Should().Be(SideRuleKind.All);
		rule.Text.Should().Be("whole side");
		rule.Covers(House("999")).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that an empty cell of a tolerance agreement means the side is not covered.
	/// </summary>
	/// <param name="raw">The cell text.</param>
	[Theory]
	[InlineData("")]
	[InlineData(null)]
	public void Parse_EmptyInToleranceList_ShouldNotCoverTheSide(string? raw)
	{
		var rule = Parse(raw, EmptyCellMeaning.NotCovered);

		rule.Kind.Should().Be(SideRuleKind.None);
		rule.Text.Should().Be("not covered");
		rule.Covers(House("1")).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a cell with only white space is still read as the whole side, like live.js does (its tolerance
	/// fallback only replaces an empty cell).
	/// </summary>
	[Fact]
	public void Parse_WhiteSpaceInToleranceList_ShouldCoverTheWholeSideLikeLiveJs()
	{
		Parse("  ", EmptyCellMeaning.NotCovered).Kind.Should().Be(SideRuleKind.All);
	}

	/// <summary>
	/// Verifies that a text starting with <c>hors</c> is not covered whatever follows and whatever the case.
	/// </summary>
	/// <param name="raw">The cell text.</param>
	[Theory]
	[InlineData("hors")]
	[InlineData("Hors secteur")]
	public void Parse_Hors_ShouldNotCoverAnyNumber(string raw)
	{
		var rule = Parse(raw);

		rule.Kind.Should().Be(SideRuleKind.None);
		rule.Text.Should().Be("not covered");
		rule.Covers(House("2")).Should().BeFalse();
	}
	#endregion

	#region Condition Tests
	/// <summary>
	/// Verifies that <c>check:</c> keeps the condition text and never covers a number.
	/// </summary>
	/// <param name="raw">The cell text.</param>
	[Theory]
	[InlineData("check: côté parc")]
	[InlineData("CHECK:côté parc")]
	public void Parse_Check_ShouldKeepTheConditionAndCoverNothing(string raw)
	{
		var rule = Parse(raw);

		rule.Kind.Should().Be(SideRuleKind.Condition);
		rule.IsCondition.Should().BeTrue();
		rule.Text.Should().Be("côté parc");
		rule.Covers(House("1")).Should().BeFalse();
	}
	#endregion

	#region Range Tests
	/// <summary>
	/// Verifies <c>17-57b</c>: the range includes 57b but not 57c, 15 or 59.
	/// </summary>
	[Fact]
	public void Parse_Range17To57b_ShouldCoverFromFirstToTheLastLetter()
	{
		var rule = Parse("17-57b");

		rule.Ranges.Should().Equal(new HouseRange(1700, 5702));
		rule.Text.Should().Be("17-57b");
		rule.Covers(House("17")).Should().BeTrue();
		rule.Covers(House("39")).Should().BeTrue();
		rule.Covers(House("57b")).Should().BeTrue();
		rule.Covers(House("57c")).Should().BeFalse();
		rule.Covers(House("15")).Should().BeFalse();
		rule.Covers(House("59")).Should().BeFalse();
	}

	/// <summary>
	/// Verifies <c>59 à fin</c>: from 59 on without end, shown as "→ end".
	/// </summary>
	[Fact]
	public void Parse_ToTheEnd_ShouldCoverEverythingFromTheNumber()
	{
		var rule = Parse("59 à fin");

		rule.Ranges.Should().Equal(new HouseRange(5900, long.MaxValue));
		rule.Text.Should().Be("59 → end");
		rule.Covers(House("59")).Should().BeTrue();
		rule.Covers(House("1001")).Should().BeTrue();
		rule.Covers(House("57")).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that "a fin" without the accent is understood too and its text is left as written.
	/// </summary>
	[Fact]
	public void Parse_ToTheEndWithoutAccent_ShouldCoverEverythingFromTheNumber()
	{
		var rule = Parse("7 a fin");

		rule.Covers(House("9")).Should().BeTrue();
		rule.Covers(House("5")).Should().BeFalse();
		rule.Text.Should().Be("7 a fin");
	}

	/// <summary>
	/// Verifies <c>2-6 et 64-76</c>: two ranges.
	/// </summary>
	[Fact]
	public void Parse_TwoRangesJoinedByEt_ShouldCoverBoth()
	{
		var rule = Parse("2-6 et 64-76");

		rule.Ranges.Should().HaveCount(2);
		Covered(rule, "2 6 64 76 8 62 78 1").Should().Equal("2", "6", "64", "76");
	}

	/// <summary>
	/// Verifies <c>1A-3</c>: a letter on the first number starts the range after the plain number.
	/// </summary>
	[Fact]
	public void Parse_RangeStartingWithALetter_ShouldExcludeThePlainFirstNumber()
	{
		var rule = Parse("1A-3");

		rule.Ranges.Should().Equal(new HouseRange(101, 399));
		rule.Covers(House("1")).Should().BeFalse();
		rule.Covers(House("1a")).Should().BeTrue();
		rule.Covers(House("3a")).Should().BeTrue();
		rule.Covers(House("4")).Should().BeFalse();
	}

	/// <summary>
	/// Verifies <c>7 et 8 et 9</c>: single numbers cover their letters too.
	/// </summary>
	[Fact]
	public void Parse_SingleNumbersJoinedByEt_ShouldCoverEachWithItsLetters()
	{
		var rule = Parse("7 et 8 et 9");

		rule.Ranges.Should().HaveCount(3);
		Covered(rule, "7 7a 8 9 10").Should().Equal("7", "7a", "8", "9");
	}

	/// <summary>
	/// Verifies that commas separate the parts, with or without a space.
	/// </summary>
	[Fact]
	public void Parse_CommaSeparatedList_ShouldCoverEachPart()
	{
		var rule = Parse("1,3, 5-9");

		rule.Ranges.Should().HaveCount(3);
		Covered(rule, "1 3 7 4").Should().Equal("1", "3", "7");
	}

	/// <summary>
	/// Verifies that a part that is not a number at all is skipped, as in live.js.
	/// </summary>
	[Fact]
	public void Parse_ListWithATextPart_ShouldSkipThatPart()
	{
		var rule = Parse("17-57b et voir panneau");

		rule.Ranges.Should().HaveCount(1);
	}

	/// <summary>
	/// Verifies that a range or "to the end" part that does not start with a number is reported as a failure
	/// (live.js would throw).
	/// </summary>
	/// <param name="raw">The cell text.</param>
	[Theory]
	[InlineData("abc-def")]
	[InlineData("abc-5")]
	[InlineData("x à fin")]
	public void Parse_RangeWithoutNumber_ShouldFailWithInvalidRange(string raw)
	{
		var result = SideRule.Parse(raw, EmptyCellMeaning.WholeSide);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(SideRule.InvalidRangeCode);
		result.Error.Message.Should().Contain(raw);
	}
	#endregion

	#region Covers Tests
	/// <summary>
	/// Verifies that a missing house number is refused.
	/// </summary>
	[Fact]
	public void Covers_Null_ShouldThrow()
	{
		var act = () => SideRule.NotCovered().Covers(null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region RuleSet Tests
	/// <summary>
	/// Verifies that the first set covering the number on its side wins.
	/// </summary>
	[Fact]
	public void FindCovering_TwoSets_ShouldReturnTheFirstThatCovers()
	{
		var sector = new RuleSet("your sector", Parse("1-9"), Parse("hors"));
		var tolerance = new RuleSet("tolerance", Parse("hors"), Parse("2-8"));
		var sets = new[] { sector, tolerance };

		RuleSet.FindCovering(sets, House("5")).Should().BeSameAs(sector);
		RuleSet.FindCovering(sets, House("4")).Should().BeSameAs(tolerance);
		RuleSet.FindCovering(sets, House("12")).Should().BeNull();
		RuleSet.FindCovering([], House("5")).Should().BeNull();
	}

	/// <summary>
	/// Verifies that the condition of a side is found, and only on that side.
	/// </summary>
	[Fact]
	public void FindCondition_ConditionOnOddSide_ShouldBeFoundOnlyThere()
	{
		var sets = new[] { new RuleSet("your sector", Parse("check: côté parc"), Parse("2-8")) };

		RuleSet.FindCondition(sets, StreetSide.Odd)!.Text.Should().Be("côté parc");
		RuleSet.FindCondition(sets, StreetSide.Even).Should().BeNull();
	}

	/// <summary>
	/// Verifies that missing rule sets are refused when looking for the covering set.
	/// </summary>
	[Fact]
	public void FindCovering_NullSets_ShouldThrow()
	{
		var act = () => RuleSet.FindCovering(null!, House("1")); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}

	/// <summary>
	/// Verifies that a missing house number is refused when looking for the covering set.
	/// </summary>
	[Fact]
	public void FindCovering_NullHouse_ShouldThrow()
	{
		var act = () => RuleSet.FindCovering([], null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}

	/// <summary>
	/// Verifies that missing rule sets are refused when looking for a condition.
	/// </summary>
	[Fact]
	public void FindCondition_NullSets_ShouldThrow()
	{
		var act = () => RuleSet.FindCondition(null!, StreetSide.Odd); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}

	/// <summary>
	/// Verifies that each side of a rule set returns its own rule.
	/// </summary>
	[Fact]
	public void RuleOf_EachSide_ShouldReturnThatSidesRule()
	{
		var set = new RuleSet("your sector", Parse("1-9"), Parse("hors"));

		set.RuleOf(StreetSide.Odd).Should().BeSameAs(set.Odd);
		set.RuleOf(StreetSide.Even).Should().BeSameAs(set.Even);
	}
	#endregion
}
