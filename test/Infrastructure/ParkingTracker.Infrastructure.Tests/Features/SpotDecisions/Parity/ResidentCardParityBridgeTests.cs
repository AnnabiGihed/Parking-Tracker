using ParkingTracker.Domain.Features.ResidentCards.Aggregates;
using ParkingTracker.Infrastructure.Features.Sectors;

namespace ParkingTracker.Infrastructure.Tests.Features.SpotDecisions.Parity;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Characterisation test of the card bridge against the parity scenarios: the card of every recorded scenario, passed
///              through <see cref="ResidentCard"/> and back with the real reference data, is the card the scenario recorded, so the
///              engine answers every scenario the same way with a card built through the aggregate.
/// </summary>
public sealed class ResidentCardParityBridgeTests
{
	#region Test Infrastructure
	/// <summary>
	/// Names every recorded scenario, whatever its kind.
	/// </summary>
	/// <returns>One row per scenario.</returns>
	public static IEnumerable<object[]> AllScenarios()
	{
		return ParityScenario.NamesOfKind("decide").Concat(ParityScenario.NamesOfKind("freeSpots")).Concat(ParityScenario.NamesOfKind("area"));
	}
	#endregion

	#region Bridge Tests
	/// <summary>
	/// Verifies that the recorded card survives the aggregate unchanged.
	/// </summary>
	/// <param name="name">The recorded scenario.</param>
	[Theory]
	[MemberData(nameof(AllScenarios))]
	public void ToParkingCard_RecordedCard_ShouldEqualTheRecordedCard(string name)
	{
		var recorded = ParityScenario.Load(name).Card;
		var municipality = new ReferenceDataProvider().GetReferenceData().Value.Municipalities.Single(m => string.Equals(m.Id, recorded.Municipality, StringComparison.Ordinal));

		var created = ResidentCard.Create(municipality, recorded);

		created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.Message : string.Empty);
		created.Value.ToParkingCard().Should().BeEquivalentTo(recorded);
	}
	#endregion
}
