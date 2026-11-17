using ParkingTracker.Domain.Features.Zones.Services;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Domain.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Domain.Tests.Features.Zones.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ZoneClassifier"/>: <c>zoneType</c> and <c>isEvent</c> of live.js.
/// </summary>
public sealed class ZoneClassifierTests
{
	#region Classify Tests
	/// <summary>
	/// Verifies that colour code 0 is free even when a rule says otherwise.
	/// </summary>
	[Fact]
	public void Classify_ColourZero_ShouldBeFreeWhateverTheRule()
	{
		ZoneClassifier.Classify(0, RuleBuilder.Create(ZoneType.Payable)).Should().Be(ZoneType.Free);
		ZoneClassifier.Classify(0, null).Should().Be(ZoneType.Free);
	}

	/// <summary>
	/// Verifies that the type of the live rule wins over the colour.
	/// </summary>
	/// <param name="colour">Colour code of the zone.</param>
	/// <param name="ruleType">The type of the rule.</param>
	/// <param name="expected">The expected zone type.</param>
	[Theory]
	[InlineData(2, ZoneType.Derogation, ZoneType.Derogation)]
	[InlineData(1, ZoneType.Disk, ZoneType.Disk)]
	[InlineData(2, ZoneType.Payable, ZoneType.Payable)]
	[InlineData(2, ZoneType.Reserved, ZoneType.Reserved)]
	[InlineData(9, ZoneType.Reserved, ZoneType.Residents)]
	[InlineData(9, ZoneType.Payable, ZoneType.Payable)]
	public void Classify_RuleWithType_ShouldUseTheRuleType(int colour, ZoneType ruleType, ZoneType expected)
	{
		ZoneClassifier.Classify(colour, RuleBuilder.Create(ruleType)).Should().Be(expected);
	}

	/// <summary>
	/// Verifies the colours that mean the same everywhere when no rule is known, and that orange stays unknown.
	/// </summary>
	/// <param name="colour">Colour code of the zone.</param>
	/// <param name="expected">The expected zone type, or <see langword="null"/>.</param>
	[Theory]
	[InlineData(1, ZoneType.Payable)]
	[InlineData(3, ZoneType.Derogation)]
	[InlineData(4, ZoneType.Derogation)]
	[InlineData(5, ZoneType.Disk)]
	[InlineData(9, ZoneType.Residents)]
	[InlineData(2, null)]
	[InlineData(6, null)]
	[InlineData(7, null)]
	[InlineData(null, null)]
	public void Classify_NoRule_ShouldUseTheColourOnly(int? colour, ZoneType? expected)
	{
		ZoneClassifier.Classify(colour, null).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that a rule without a type falls back to the colour.
	/// </summary>
	[Fact]
	public void Classify_RuleWithoutType_ShouldFallBackToTheColour()
	{
		ZoneClassifier.Classify(5, RuleBuilder.Create(type: null)).Should().Be(ZoneType.Disk);
		ZoneClassifier.Classify(2, RuleBuilder.Create(type: null)).Should().BeNull();
	}
	#endregion

	#region IsEvent Tests
	/// <summary>
	/// Verifies that colour code 6 and a rule named after an event are event zones.
	/// </summary>
	[Fact]
	public void IsEvent_ColourSixOrEventRuleName_ShouldBeTrue()
	{
		var named = RuleBuilder.Create() with { Name = "Zone événement" };
		var unaccented = RuleBuilder.Create() with { Name = "ZONE EVENEMENT" };

		ZoneClassifier.IsEvent(6, null).Should().BeTrue();
		ZoneClassifier.IsEvent(2, named).Should().BeTrue();
		ZoneClassifier.IsEvent(2, unaccented).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that other zones, rules without a name and missing rules are not event zones.
	/// </summary>
	[Fact]
	public void IsEvent_OrdinaryZone_ShouldBeFalse()
	{
		ZoneClassifier.IsEvent(2, RuleBuilder.Create() with { Name = "Zone rouge" }).Should().BeFalse();
		ZoneClassifier.IsEvent(2, RuleBuilder.Create()).Should().BeFalse();
		ZoneClassifier.IsEvent(null, null).Should().BeFalse();
	}
	#endregion
}
