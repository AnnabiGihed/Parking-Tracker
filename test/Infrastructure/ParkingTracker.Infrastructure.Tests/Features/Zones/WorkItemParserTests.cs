using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Zones;

namespace ParkingTracker.Infrastructure.Tests.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="WorkItemParser"/>: which roadworks and events are running at a moment, how their
///              fields are read and whether their geometry is points or a surface.
/// </summary>
public sealed class WorkItemParserTests
{
	#region Test Infrastructure
	/// <summary>
	/// The moment the tests run at.
	/// </summary>
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 11, 0, 0, TimeSpan.FromHours(1));

	/// <summary>
	/// A point as GeoJSON.
	/// </summary>
	private const string Point = "{\"type\":\"Point\",\"coordinates\":[485000,6590000]}";

	/// <summary>
	/// Wraps one feature per property text in a feature collection.
	/// </summary>
	/// <param name="geometry">Geometry JSON shared by every feature.</param>
	/// <param name="properties">The properties of the feature.</param>
	/// <returns>Collection JSON.</returns>
	private static string Collection(string geometry, params string[] properties)
	{
		return "{\"features\":[" + string.Join(",", properties.Select(p => $"{{\"geometry\":{geometry},\"properties\":{{{p}}}}}")) + "]}";
	}
	#endregion

	#region Roadworks Tests
	/// <summary>
	/// Verifies that sites that ended, or have not started, are left out and that the running ones keep their fields.
	/// </summary>
	[Fact]
	public void ParseRoadworks_Sites_ShouldKeepOnlyTheRunningOnes()
	{
		var json = Collection(
			Point,
			"\"nature_fr\":\"Travaux\",\"road_impl_fr\":\"Rue du Méridien\",\"str_start_date\":\"01/10/2026\",\"str_end_date\":\"18/12/2026\"",
			"\"nature_fr\":\"Fini\",\"str_end_date\":\"16/11/2026\"",
			"\"nature_fr\":\"Bientôt\",\"str_start_date\":\"18/11/2026\"",
			"\"str_end_date\":\"17/11/2026\"");

		var result = WorkItemParser.ParseRoadworks(json, Now);

		result.Value.Select(item => item.Title).Should().Equal("Travaux", "Roadworks");
		var first = result.Value[0];
		(first.Kind, first.Where, first.To).Should().Be((WorkKind.Works, "Rue du Méridien", "2026-12-18"));
		first.Points.Should().ContainSingle();
		first.Surface.Should().BeNull();
	}

	/// <summary>
	/// Verifies that the place name falls back to the second field when the first is missing.
	/// </summary>
	[Fact]
	public void ParseRoadworks_NoRoadName_ShouldUseTheNameField()
	{
		var json = Collection(Point, "\"nature_fr\":\"Travaux\",\"nom_fr\":\"Chantier X\"");

		WorkItemParser.ParseRoadworks(json, Now).Value.Single().Where.Should().Be("Chantier X");
	}

	/// <summary>
	/// Verifies that an area-shaped site has a surface and no points.
	/// </summary>
	[Fact]
	public void ParseRoadworks_PolygonSite_ShouldHaveASurface()
	{
		const string polygon = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}";

		var item = WorkItemParser.ParseRoadworks(Collection(polygon, "\"nature_fr\":\"Travaux\""), Now).Value.Single();

		item.Points.Should().BeNull();
		item.Surface!.Polygons.Should().HaveCount(1);
	}
	#endregion

	#region Event Tests
	/// <summary>
	/// Verifies that inactive events, events that ended and events that have not started are left out.
	/// </summary>
	[Fact]
	public void ParseEvents_Events_ShouldKeepOnlyTheRunningOnes()
	{
		var json = Collection(
			Point,
			"\"type_fr\":\" Manifestation \",\"location_fr\":\"Place X\",\"start_time\":\"2026-11-17T08:00:00Z\",\"end_time\":\"2026-11-18T20:00:00Z\"",
			"\"type_fr\":\"Inactif\",\"is_active\":false",
			"\"type_fr\":\"Passé\",\"end_time\":\"2026-11-17T09:59:59Z\"",
			"\"type_fr\":\"Futur\",\"start_time\":\"2026-11-17T10:00:01Z\"");

		var result = WorkItemParser.ParseEvents(json, Now);

		var item = result.Value.Should().ContainSingle().Subject;
		(item.Kind, item.Title, item.Where, item.To).Should().Be((WorkKind.Event, "Manifestation", "Place X", "2026-11-18"));
	}

	/// <summary>
	/// Verifies that an answer that is not a feature collection is a bad-response failure naming the data.
	/// </summary>
	[Fact]
	public void ParseEvents_UnexpectedAnswer_ShouldFail()
	{
		var result = WorkItemParser.ParseEvents("[]", Now);

		result.Error.Code.Should().Be("Mobility.BadResponse");
		result.Error.Message.Should().Be("traffic events: unexpected answer");
	}
	#endregion
}
