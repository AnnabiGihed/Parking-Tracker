using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.ReferenceData;
using ParkingTracker.Domain.Tests.TestDoubles;
using ParkingTracker.Domain.Zones;

namespace ParkingTracker.Domain.Tests.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CardScopeEvaluator"/>: the scope of a card per municipality kind, border zones and the
///              limit of an area.
/// </summary>
public sealed class CardScopeEvaluatorTests
{
	#region Test Infrastructure
	private static SectorMaille Area(string code, double northOfHereMetres)
	{
		var center = new GeoPoint(ZoneBuilder.Here.Latitude + (northOfHereMetres / 111320), ZoneBuilder.Here.Longitude);
		return new SectorMaille(code, "Secteur " + code, ZoneBuilder.Surface("area", null, center: center).Shape);
	}

	private static CardScope Evaluate(DecisionContext context, SectorMaille[] mailles, double accuracy = 0, string municipality = "IXELLES")
	{
		var surface = ZoneBuilder.Surface("Rue Test", 4, municipality: municipality);
		return CardScopeEvaluator.Evaluate(context, surface, ZoneBuilder.Here, mailles, accuracy);
	}
	#endregion

	#region Area Tests
	/// <summary>
	/// Verifies that a place inside an area of the card is covered, naming the area.
	/// </summary>
	[Fact]
	public void Evaluate_InsideAnAreaOfTheCard_ShouldBeYes()
	{
		var context = ZoneBuilder.Context(MunicipalityKind.Mailles, ZoneBuilder.Card(mailles: ["A1"]));

		var scope = Evaluate(context, [Area("A1", 0)]);

		scope.Should().Be(CardScope.Yes("in your area Secteur A1"));
	}

	/// <summary>
	/// Verifies that a place well outside the areas of the card is not covered.
	/// </summary>
	[Fact]
	public void Evaluate_OutsideTheAreasOfTheCard_ShouldBeNo()
	{
		var context = ZoneBuilder.Context(MunicipalityKind.Mailles, ZoneBuilder.Card(mailles: ["A1"]));

		var scope = Evaluate(context, [Area("A1", 500), Area("OTHER", 0)]);

		scope.Should().Be(CardScope.No("is outside the areas on your card"));
	}

	/// <summary>
	/// Verifies that a place within 12 m of an area of the card is at its limit.
	/// </summary>
	[Fact]
	public void Evaluate_JustOutsideAnArea_ShouldAskToCheckTheSigns()
	{
		var context = ZoneBuilder.Context(MunicipalityKind.Mailles, ZoneBuilder.Card(mailles: ["A1"]));

		var scope = Evaluate(context, [Area("A1", 25)]);

		scope.State.Should().Be(CardScopeState.Check);
		scope.Why.Should().Be("you are at the limit of your area Secteur A1: check the street signs");
	}

	/// <summary>
	/// Verifies that a poor GPS accuracy widens the limit of an area.
	/// </summary>
	[Fact]
	public void Evaluate_ThirtyMetresFromAnAreaWithPoorAccuracy_ShouldAskToCheckTheSigns()
	{
		var context = ZoneBuilder.Context(MunicipalityKind.Mailles, ZoneBuilder.Card(mailles: ["A1"]));

		var scope = Evaluate(context, [Area("A1", 45)], accuracy: 25);

		scope.State.Should().Be(CardScopeState.Check);
	}

	/// <summary>
	/// Verifies that a border zone of the card covers its place whatever the municipality.
	/// </summary>
	[Fact]
	public void Evaluate_InsideABorderZoneOfTheCard_ShouldBeYes()
	{
		var context = ZoneBuilder.Context(MunicipalityKind.Mailles, ZoneBuilder.Card(lims: ["LIM1"]));

		var scope = Evaluate(context, [Area("LIM1", 0)], municipality: "UCCLE");

		scope.Should().Be(CardScope.Yes("border zone Secteur LIM1 (on your card)"));
	}
	#endregion

	#region Municipality Kind Tests
	/// <summary>
	/// Verifies that another municipality is not covered, except for the City of Brussels, whose tolerance agreements are street lists.
	/// </summary>
	/// <param name="kind">The kind of the card's municipality.</param>
	/// <param name="expected">The expected state.</param>
	[Theory]
	[InlineData(MunicipalityKind.Whole, CardScopeState.No)]
	[InlineData(MunicipalityKind.Mailles, CardScopeState.No)]
	[InlineData(MunicipalityKind.Streets, CardScopeState.Streets)]
	public void Evaluate_AnotherMunicipality_ShouldDependOnTheKind(MunicipalityKind kind, CardScopeState expected)
	{
		var context = ZoneBuilder.Context(kind, ZoneBuilder.Card(mailles: ["A1"]));

		var scope = Evaluate(context, [Area("A1", 0)], municipality: "UCCLE");

		scope.State.Should().Be(expected);
	}

	/// <summary>
	/// Verifies that a Woluwe-Saint-Lambert style card is valid in blue zones only.
	/// </summary>
	/// <param name="colour">The colour code of the surface.</param>
	/// <param name="expected">The expected state.</param>
	[Theory]
	[InlineData(5, CardScopeState.Yes)]
	[InlineData(4, CardScopeState.Check)]
	public void Evaluate_BlueZoneKind_ShouldCoverBlueZonesOnly(int colour, CardScopeState expected)
	{
		var context = ZoneBuilder.Context(MunicipalityKind.Wsl);
		var surface = ZoneBuilder.Surface("Rue Test", colour);

		var scope = CardScopeEvaluator.Evaluate(context, surface, ZoneBuilder.Here, []);

		scope.State.Should().Be(expected);
	}
	#endregion
}
