using ParkingTracker.Infrastructure.Features.Zones;

namespace ParkingTracker.Infrastructure.Tests.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the parsers of point data: address points, parking spaces and sector areas.
/// </summary>
public sealed class PointParsersTests
{
	#region Address Point Tests
	/// <summary>
	/// Verifies that address points are read with a trimmed number, and that features that are not points or have no readable number are left out.
	/// </summary>
	[Fact]
	public void AddressPointParser_Features_ShouldKeepReadablePoints()
	{
		const string json = "{\"features\":["
			+ "{\"geometry\":{\"type\":\"Point\",\"coordinates\":[485000,6590000]},\"properties\":{\"adrn\":\" 12b \"}},"
			+ "{\"geometry\":{\"type\":\"Point\",\"coordinates\":[485001,6590001]},\"properties\":{\"adrn\":216}},"
			+ "{\"geometry\":{\"type\":\"Point\",\"coordinates\":[485002,6590002]},\"properties\":{\"adrn\":\"sans numéro\"}},"
			+ "{\"geometry\":{\"type\":\"MultiPoint\",\"coordinates\":[[485003,6590003]]},\"properties\":{\"adrn\":\"14\"}},"
			+ "{\"geometry\":null,\"properties\":{\"adrn\":\"16\"}}]}";

		var result = AddressPointParser.Parse(json);

		result.Value.Select(point => point.Text).Should().Equal("12b", "216");
		result.Value[0].House.Letter.Should().Be("b");
		result.Value[0].Position.X.Should().Be(485000);
	}

	/// <summary>
	/// Verifies that an unexpected answer is a bad-response failure from UrbIS.
	/// </summary>
	[Fact]
	public void AddressPointParser_UnexpectedAnswer_ShouldFail()
	{
		var result = AddressPointParser.Parse("{}");

		result.Error.Code.Should().Be("Urbis.BadResponse");
		result.Error.Message.Should().Be("UrbIS: unexpected answer");
	}

	/// <summary>
	/// Verifies that a position with fewer than two numbers is a bad-response failure, not an unhandled exception.
	/// </summary>
	[Fact]
	public void AddressPointParser_ShortPosition_ShouldFail()
	{
		const string json = "{\"features\":[{\"geometry\":{\"type\":\"Point\",\"coordinates\":[485000]},\"properties\":{\"adrn\":\"12\"}}]}";

		var result = AddressPointParser.Parse(json);

		result.Error.Code.Should().Be("Urbis.BadResponse");
	}
	#endregion

	#region Parking Space Tests
	/// <summary>
	/// Verifies that every point of a multi-point feature is a space with the reservation type, which may arrive as text or be missing.
	/// </summary>
	[Fact]
	public void ReservedSpaceParser_Features_ShouldGiveOneSpacePerPoint()
	{
		const string json = "{\"features\":["
			+ "{\"geometry\":{\"type\":\"MultiPoint\",\"coordinates\":[[485000,6590000],[485010,6590010]]},\"properties\":{\"typres\":1}},"
			+ "{\"geometry\":{\"type\":\"Point\",\"coordinates\":[485020,6590020]},\"properties\":{\"typres\":\"16\"}},"
			+ "{\"geometry\":{\"type\":\"Point\",\"coordinates\":[485030,6590030]},\"properties\":{}}]}";

		var result = ReservedSpaceParser.Parse(json);

		result.Value.Select(space => space.ReservationType).Should().Equal(1, 1, 16, 0);
		result.Value[0].Label.Should().Be("🚚 Delivery space");
		result.Value[2].IsTimeLimited.Should().BeTrue();
		result.Value[3].IsReserved.Should().BeFalse();
	}
	#endregion

	#region Sector Area Tests
	/// <summary>
	/// Verifies that sector areas are read with their code and French name.
	/// </summary>
	[Fact]
	public void SectorMailleParser_Features_ShouldReadCodeAndName()
	{
		const string json = "{\"features\":[{\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]},\"properties\":{\"zonecode\":\"IXLR_Ouest\",\"namefre\":\"Secteur Ouest\"}}]}";

		var maille = SectorMailleParser.Parse(json).Value.Single();

		(maille.ZoneCode, maille.NameFrench, maille.DisplayName).Should().Be(("IXLR_Ouest", "Secteur Ouest", "Secteur Ouest"));
	}
	#endregion
}
