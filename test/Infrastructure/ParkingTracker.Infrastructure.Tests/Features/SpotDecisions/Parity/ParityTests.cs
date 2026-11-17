using System.Text.Json;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Application.Features.SpotDecisions.Queries.GetAreaStatuses;
using ParkingTracker.Infrastructure.Tests.Features.SpotDecisions.TestDoubles;

namespace ParkingTracker.Infrastructure.Tests.Features.SpotDecisions.Parity;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Parity tests: for every place recorded from <c>live.js</c> (<c>tools/parity/record.mjs</c>), the production
///              parsers, decider and query handlers, fed with the recorded responses and the recorded clock, give the same
///              verdict, title and plain-text texts as <c>live.js</c>, ask for exactly the same requests, and reach the same
///              result of the official-document check.
/// </summary>
public sealed class ParityTests
{
	#region Constants
	/// <summary>
	/// The reason given when two coordinates differ slightly.
	/// </summary>
	private const string CoordinateToleranceMessage = "the recorded and the computed positions differ by less than a micrometre of arc";
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Names the recorded <c>decide</c> scenarios, for the theory below.
	/// </summary>
	/// <returns>One row per scenario.</returns>
	public static IEnumerable<object[]> DecideScenarios() => ParityScenario.NamesOfKind("decide");

	/// <summary>
	/// Names the recorded <c>freeSpots</c> scenarios, for the theory below.
	/// </summary>
	/// <returns>One row per scenario.</returns>
	public static IEnumerable<object[]> FreeSpotScenarios() => ParityScenario.NamesOfKind("freeSpots");

	/// <summary>
	/// Names the recorded <c>area</c> scenarios, for the theory below.
	/// </summary>
	/// <returns>One row per scenario.</returns>
	public static IEnumerable<object[]> AreaScenarios() => ParityScenario.NamesOfKind("area");

	/// <summary>
	/// Asserts that the same requests were made as in the recording.
	/// </summary>
	/// <param name="harness">Harness that replays the recorded scenario.</param>
	private static void AssertSameRequests(ParityHarness harness)
	{
		harness.Sources.Requested.Should().Equal(harness.Scenario.Requests, "the C# code must ask for the same requests as live.js, in the same order, without a refetch");
	}

	/// <summary>
	/// Asserts that the official list check gave the recorded outcome.
	/// </summary>
	/// <param name="harness">Harness that replays the recorded scenario.</param>
	/// <returns>A task that completes when the outcome was compared.</returns>
	private static async Task AssertSameOfficialListAsync(ParityHarness harness)
	{
		var expected = harness.Scenario.OfficialList;
		if (expected.ValueKind != JsonValueKind.Object)
		{
			return;
		}

		var context = await harness.Contexts.CreateAsync(harness.Clock.GetLocalNow(), CancellationToken.None).ConfigureAwait(false);
		var actual = context.Value.OfficialList!;
		actual.State.ToString().ToLowerInvariant().Should().Be(expected.GetProperty("status").GetString());
		actual.Document.Should().Be(expected.GetProperty("document").GetString());
		actual.DocumentDate.Should().Be(expected.GetProperty("date").GetString());
		actual.Why.Should().Be(expected.GetProperty("why").GetString());
	}
	#endregion

	#region Decide Tests
	/// <summary>
	/// Verifies that a recorded decision gives the verdict, title, detail, notes, tariff and zone <c>live.js</c> gave.
	/// </summary>
	/// <param name="name">The recorded scenario.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[MemberData(nameof(DecideScenarios))]
	public async Task DecideSpot_RecordedScenario_ShouldGiveTheAnswerOfLiveJs(string name)
	{
		var harness = new ParityHarness(ParityScenario.Load(name));
		var handler = new DecideSpotQueryHandler(harness.Contexts, harness.Runner, new StaticNetworkStatus(true), harness.Clock);
		var point = harness.Scenario.Point;

		var result = await handler.Handle(new DecideSpotQuery(point.Latitude, point.Longitude, harness.Scenario.Accuracy), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		var expected = harness.Scenario.Expected;
		var decision = result.Value;
		decision.Status.ToString().ToLowerInvariant().Should().Be(expected.GetProperty("status").GetString());
		decision.Title.Should().Be(expected.GetProperty("title").GetString());
		decision.Detail.Should().Be(expected.GetProperty("detail").GetString());
		decision.Notes.Should().Equal(expected.GetProperty("notes").EnumerateArray().Select(note => note.GetString()));
		decision.Tariff.Should().Be(expected.GetProperty("tariff").GetString());
		decision.Zone?.Surface.Id.Should().Be(IdentifierOf(expected.GetProperty("zoneId")));
		decision.Zone?.Surface.FrenchName.Should().Be(expected.GetProperty("zoneStreet").GetString());
		(decision.Zone is null).Should().Be(expected.GetProperty("zoneId").ValueKind == JsonValueKind.Null);
		AssertSameRequests(harness);
		await AssertSameOfficialListAsync(harness);
	}
	#endregion

	#region Free Spot Tests
	/// <summary>
	/// Verifies that the free-spot search finds the spots <c>live.js</c> found, in the same order.
	/// </summary>
	/// <param name="name">The recorded scenario.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[MemberData(nameof(FreeSpotScenarios))]
	public async Task FindFreeSpots_RecordedScenario_ShouldFindTheSpotsOfLiveJs(string name)
	{
		var harness = new ParityHarness(ParityScenario.Load(name));
		var handler = new FindFreeSpotsQueryHandler(harness.Contexts, harness.Areas, harness.Clock);
		var point = harness.Scenario.Point;

		var result = await handler.Handle(new FindFreeSpotsQuery(point.Latitude, point.Longitude), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		var expected = harness.Scenario.Expected.EnumerateArray().ToList();
		result.Value.Should().HaveCount(expected.Count);
		for (var i = 0; i < expected.Count; i++)
		{
			var spot = result.Value[i];
			spot.Street.Should().Be(expected[i].GetProperty("street").GetString());
			spot.Commune.Should().Be(expected[i].GetProperty("commune").GetString());
			spot.Label.Should().Be(expected[i].GetProperty("label").GetString());
			spot.Point.Latitude.Should().BeApproximately(expected[i].GetProperty("lat").GetDouble(), 1e-9, CoordinateToleranceMessage);
			spot.Point.Longitude.Should().BeApproximately(expected[i].GetProperty("lon").GetDouble(), 1e-9, CoordinateToleranceMessage);
			spot.Metres.Should().BeApproximately(expected[i].GetProperty("metres").GetDouble(), 1e-6);
		}

		AssertSameRequests(harness);
		await AssertSameOfficialListAsync(harness);
	}
	#endregion

	#region Area Tests
	/// <summary>
	/// Verifies that the surfaces of an area get the kind, colour and covered house numbers <c>live.js</c> gave them.
	/// </summary>
	/// <param name="name">The recorded scenario.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[MemberData(nameof(AreaScenarios))]
	public async Task GetAreaStatuses_RecordedScenario_ShouldGiveTheSurfaceStatusesOfLiveJs(string name)
	{
		var harness = new ParityHarness(ParityScenario.Load(name));
		var handler = new GetAreaStatusesQueryHandler(harness.Contexts, harness.Areas, harness.Clock);
		var point = harness.Scenario.Point;
		const double delta = 0.0012;

		var result = await handler.Handle(
			new GetAreaStatusesQuery(point.Latitude - delta, point.Longitude - (delta * 1.6), point.Latitude + delta, point.Longitude + (delta * 1.6)),
			CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		var expected = harness.Scenario.Expected.EnumerateArray().ToList();
		result.Value.Should().HaveCount(expected.Count);
		for (var i = 0; i < expected.Count; i++)
		{
			var status = result.Value[i];
			status.Surface.Id.Should().Be(IdentifierOf(expected[i].GetProperty("id")));
			status.Kind.ToString().ToLowerInvariant().Should().Be(expected[i].GetProperty("kind").GetString());
			status.Color.Should().Be(expected[i].GetProperty("color").GetString());
			status.Surface.FrenchName.Should().Be(expected[i].GetProperty("street").GetString());
			status.Street?.FrenchName.Should().Be(expected[i].GetProperty("label").GetString());
			(status.Street is null).Should().Be(expected[i].GetProperty("label").ValueKind == JsonValueKind.Null);
			status.Near.Should().HaveCount(expected[i].GetProperty("near").GetInt32());
			status.Near.Count(near => near.IsCovered).Should().Be(expected[i].GetProperty("covered").GetInt32());
		}

		AssertSameRequests(harness);
		await AssertSameOfficialListAsync(harness);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads an identifier that may be null.
	/// </summary>
	/// <param name="value">JSON value of the identifier.</param>
	/// <returns>The identifier, or <see langword="null"/>.</returns>
	private static string? IdentifierOf(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetRawText();
	#endregion
}
