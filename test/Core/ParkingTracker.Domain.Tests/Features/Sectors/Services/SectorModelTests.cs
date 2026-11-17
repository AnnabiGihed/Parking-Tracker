using ParkingTracker.Domain.Features.Sectors.Services;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.Sectors.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SectorModel"/> and <see cref="StreetRules"/>: building the street rules of a
///              sector, finding a street by its French or Dutch name and selecting the rule sets that apply
///              (<c>buildSector</c>, <c>streetIndex</c>, <c>ruleSetsFor</c> of live.js).
/// </summary>
public sealed class SectorModelTests
{
	#region Constants
	/// <summary>
	/// The code of the Louise sector.
	/// </summary>
	private const string LouiseCode = "BXL_LIM_IXL_LOUISE";
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Creates a sector definition from street rows, without tolerance agreements.
	/// </summary>
	/// <param name="streets">Street rows of the sector.</param>
	/// <returns>Sector definition.</returns>
	private static SectorDefinition Definition(params SectorStreet[] streets) => Definition(streets, []);

	/// <summary>
	/// Creates a sector definition from street rows and tolerance agreements.
	/// </summary>
	/// <param name="streets">Street rows of the sector.</param>
	/// <param name="tolerances">Tolerance agreements of the sector.</param>
	/// <returns>Sector definition.</returns>
	private static SectorDefinition Definition(SectorStreet[] streets, ToleranceAgreement[] tolerances)
	{
		return new SectorDefinition("louise", "Louise", "Louiza", "bruxelles", null, [], streets, tolerances);
	}

	/// <summary>
	/// Creates a street row that belongs to the sector list.
	/// </summary>
	/// <param name="french">French street name.</param>
	/// <param name="dutch">Dutch street name.</param>
	/// <param name="odd">Cell text for the odd side of the street.</param>
	/// <param name="even">Cell text for the even side of the street.</param>
	/// <returns>Sector street row.</returns>
	private static SectorStreet SectorRow(string french, string dutch, string odd, string even) => new(french, dutch, odd, even, null, true);

	/// <summary>
	/// Creates a street row that belongs to a tolerance agreement.
	/// </summary>
	/// <param name="french">French street name.</param>
	/// <param name="dutch">Dutch street name.</param>
	/// <param name="odd">Cell text for the odd side of the street.</param>
	/// <param name="even">Cell text for the even side of the street.</param>
	/// <returns>Tolerance street row.</returns>
	private static SectorStreet ToleranceRow(string french, string dutch, string odd, string even) => new(french, dutch, odd, even, null, false);

	/// <summary>
	/// Builds the sector the lookup tests run against.
	/// </summary>
	/// <returns>Sector model.</returns>
	private static SectorModel Sample()
	{
		var definition = Definition(
			[
				SectorRow("Avenue Louise", "Louizalaan", string.Empty, "2-6"),
				SectorRow("Rue Royale", "Koningsstraat", "hors", "hors"),
			],
			[
				new ToleranceAgreement(
					LouiseCode,
					"Ixelles Louise",
					"Ixelles",
					[ToleranceRow("Avenue Louise", "Louizalaan", "1-9", string.Empty), ToleranceRow("Rue Neuve", "Nieuwstraat", "check: côté parc", "2")]),
			]);
		var result = SectorModel.Build(definition);
		result.IsSuccess.Should().BeTrue();
		return result.Value;
	}
	#endregion

	#region Build Tests
	/// <summary>
	/// Verifies that sector streets and tolerance-only streets are merged by French name in first-seen order.
	/// </summary>
	[Fact]
	public void Build_SectorWithTolerance_ShouldMergeStreetsByFrenchName()
	{
		var model = Sample();

		model.Streets.Select(s => s.FrenchName).Should().Equal("Avenue Louise", "Rue Royale", "Rue Neuve");
		model.Streets.Select(s => s.Index).Should().Equal(0, 1, 2);
	}

	/// <summary>
	/// Verifies the rules of a sector street: an empty cell is the whole side, and a tolerance row on the same street is attached to it.
	/// </summary>
	[Fact]
	public void Build_SectorStreet_ShouldParseRulesWithTheRowsEmptyCellMeaning()
	{
		var louise = Sample().Streets[0];

		louise.InSector.Should().BeTrue();
		louise.Odd.IsAll.Should().BeTrue();
		louise.Even.Kind.Should().Be(SideRuleKind.Ranges);
		louise.Tolerances.Should().ContainSingle();
		louise.Tolerances[0].Code.Should().Be(LouiseCode);
		louise.Tolerances[0].Odd.Kind.Should().Be(SideRuleKind.Ranges);
		louise.Tolerances[0].Even.Kind.Should().Be(SideRuleKind.None);
	}

	/// <summary>
	/// Verifies that a street only a tolerance lists is outside the sector with both sides not covered.
	/// </summary>
	[Fact]
	public void Build_ToleranceOnlyStreet_ShouldBeOutsideTheSector()
	{
		var neuve = Sample().Streets[2];

		neuve.InSector.Should().BeFalse();
		neuve.Odd.Kind.Should().Be(SideRuleKind.None);
		neuve.Even.Kind.Should().Be(SideRuleKind.None);
		neuve.Tolerances.Single().Odd.IsCondition.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a rule that cannot be parsed fails the build and names the sector and the street.
	/// </summary>
	[Fact]
	public void Build_InvalidSectorRule_ShouldFailWithInvalidRule()
	{
		var result = SectorModel.Build(Definition(SectorRow("Rue Haute", "Hoogstraat", "abc-def", "2")));

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(SectorModel.InvalidRuleCode);
		result.Error.Message.Should().Contain("louise").And.Contain("Rue Haute");
	}

	/// <summary>
	/// Verifies that an invalid even-side rule fails the build.
	/// </summary>
	[Fact]
	public void Build_InvalidEvenRule_ShouldFailWithInvalidRule()
	{
		var result = SectorModel.Build(Definition(SectorRow("Rue Haute", "Hoogstraat", "1", "x à fin")));

		result.Error.Code.Should().Be(SectorModel.InvalidRuleCode);
	}

	/// <summary>
	/// Verifies that an invalid tolerance rule fails the build.
	/// </summary>
	[Fact]
	public void Build_InvalidToleranceRule_ShouldFailWithInvalidRule()
	{
		var result = SectorModel.Build(Definition(
			[],
			[new ToleranceAgreement("C", "L", "Ixelles", [ToleranceRow("Rue Basse", "Laagstraat", "1", "abc-5")])]));

		result.Error.Code.Should().Be(SectorModel.InvalidRuleCode);
	}

	/// <summary>
	/// Verifies that a missing definition is refused.
	/// </summary>
	[Fact]
	public void Build_Null_ShouldThrow()
	{
		var act = () => SectorModel.Build(null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region FindStreet Tests
	/// <summary>
	/// Verifies that a street is found by its French name, its Dutch name, a bilingual surface name or a loose spelling.
	/// </summary>
	/// <param name="name">The name to look for.</param>
	[Theory]
	[InlineData("Avenue Louise")]
	[InlineData("LOUIZALAAN")]
	[InlineData("Avenue Louise - Louizalaan")]
	[InlineData("avenue-louise")]
	public void FindStreet_KnownName_ShouldFindTheStreet(string name)
	{
		Sample().FindStreet(name)!.FrenchName.Should().Be("Avenue Louise");
	}

	/// <summary>
	/// Verifies that the names are tried in order and a missing name is skipped.
	/// </summary>
	[Fact]
	public void FindStreet_SeveralNames_ShouldUseTheFirstThatMatches()
	{
		Sample().FindStreet(null, "Unknown", "Koningsstraat")!.FrenchName.Should().Be("Rue Royale");
	}

	/// <summary>
	/// Verifies that an unknown name finds nothing.
	/// </summary>
	[Fact]
	public void FindStreet_UnknownName_ShouldReturnNull()
	{
		Sample().FindStreet("Rue Inconnue").Should().BeNull();
	}

	/// <summary>
	/// Verifies that missing names are refused.
	/// </summary>
	[Fact]
	public void FindStreet_NullNames_ShouldThrow()
	{
		var act = () => Sample().FindStreet(null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region RuleSetsFor Tests
	/// <summary>
	/// Verifies that sector rules apply on the City of Brussels territory, whatever the spelling of the municipality.
	/// </summary>
	/// <param name="municipality">The municipality of the surface.</param>
	[Theory]
	[InlineData("Bruxelles")]
	[InlineData("BRUXELLES")]
	public void RuleSetsFor_CityOfBrussels_ShouldReturnTheSectorRules(string municipality)
	{
		var sets = Sample().Streets[0].RuleSetsFor(municipality, new HashSet<string>(StringComparer.Ordinal));

		sets.Select(s => s.Label).Should().Equal("your sector");
	}

	/// <summary>
	/// Verifies that a tolerance applies only on its own municipality and only with the card's code.
	/// </summary>
	[Fact]
	public void RuleSetsFor_IxellesWithCode_ShouldReturnOnlyTheTolerance()
	{
		var louise = Sample().Streets[0];

		louise.RuleSetsFor("Ixelles", new HashSet<string>(StringComparer.Ordinal) { LouiseCode }).Select(s => s.Label).Should().Equal("Ixelles Louise");
		louise.RuleSetsFor("Ixelles", new HashSet<string>(StringComparer.Ordinal)).Should().BeEmpty();
		louise.RuleSetsFor("Ixelles", new HashSet<string>(StringComparer.Ordinal) { "OTHER" }).Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a street in the sector keeps its sector rules on the City territory, whatever the codes of the card.
	/// </summary>
	[Fact]
	public void RuleSetsFor_SectorStreetInTheCity_ShouldReturnOnlyTheSectorRulesEvenWithTheCode()
	{
		Sample().Streets[0].RuleSetsFor("Bruxelles", new HashSet<string>(StringComparer.Ordinal) { LouiseCode }).Select(s => s.Label).Should().Equal("your sector");
	}

	/// <summary>
	/// Verifies that a street outside the sector has no sector rules on the City territory.
	/// </summary>
	[Fact]
	public void RuleSetsFor_ToleranceOnlyStreetInTheCity_ShouldReturnNothing()
	{
		Sample().Streets[2].RuleSetsFor("Bruxelles", new HashSet<string>(StringComparer.Ordinal) { LouiseCode }).Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a tolerance-only street gets its tolerance in the municipality of the agreement.
	/// </summary>
	[Fact]
	public void RuleSetsFor_ToleranceOnlyStreetInItsMunicipality_ShouldReturnTheTolerance()
	{
		Sample().Streets[2].RuleSetsFor("Ixelles", new HashSet<string>(StringComparer.Ordinal) { LouiseCode }).Should().ContainSingle();
	}

	/// <summary>
	/// Verifies that another municipality gets no rules.
	/// </summary>
	[Fact]
	public void RuleSetsFor_OtherMunicipality_ShouldReturnNothing()
	{
		Sample().Streets[0].RuleSetsFor("Uccle", new HashSet<string>(StringComparer.Ordinal) { LouiseCode }).Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that an unknown municipality gets no rules.
	/// </summary>
	[Fact]
	public void RuleSetsFor_NoMunicipality_ShouldReturnNothing()
	{
		Sample().Streets[0].RuleSetsFor(null, new HashSet<string>(StringComparer.Ordinal) { LouiseCode }).Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that missing card codes are refused.
	/// </summary>
	[Fact]
	public void RuleSetsFor_NullCodes_ShouldThrow()
	{
		var act = () => Sample().Streets[0].RuleSetsFor("Bruxelles", null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
