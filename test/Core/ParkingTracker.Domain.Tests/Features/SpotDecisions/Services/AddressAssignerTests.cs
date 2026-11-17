using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Domain.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.Domain.Tests.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="AddressAssigner"/>: which street an address point is tied to, and which points are left
///              out (far from every surface, corners, misplaced numbers).
/// </summary>
public sealed class AddressAssignerTests
{
	#region Test Infrastructure
	/// <summary>
	/// Creates an address point with the given house number and position.
	/// </summary>
	/// <param name="number">House number of the address.</param>
	/// <param name="at">Position of the address.</param>
	/// <returns>Address point.</returns>
	private static AddressPoint Point(int number, GeoPoint at)
	{
		return new AddressPoint(number.ToString(System.Globalization.CultureInfo.InvariantCulture), new HouseNumber(number, string.Empty), WebMercator.ToMercator(at));
	}

	/// <summary>
	/// Creates a position the given distance north of the standard position.
	/// </summary>
	/// <param name="metres">Distance north of the reference point in metres.</param>
	/// <returns>Geographic position.</returns>
	private static GeoPoint North(double metres) => new(ZoneBuilder.Here.Latitude + (metres / 111320), ZoneBuilder.Here.Longitude);
	#endregion

	#region Assignment Tests
	/// <summary>
	/// Verifies that an address on a surface is tied to its street, municipality and surface.
	/// </summary>
	[Fact]
	public void Assign_AddressOnASurface_ShouldTieItToThatStreet()
	{
		var surface = ZoneBuilder.Surface("Rue Léopold", 4, id: "42");

		var result = AddressAssigner.Assign([Point(12, ZoneBuilder.Here)], [surface], null);

		var address = result.Should().ContainSingle().Subject;
		address.Street.Should().Be("rue leopold");
		address.Commune.Should().Be("ixelles");
		address.SurfaceId.Should().Be("42");
		address.StreetIndex.Should().Be(-1);
	}

	/// <summary>
	/// Verifies that an address more than 35 m from every surface is left out.
	/// </summary>
	[Fact]
	public void Assign_AddressFarFromEverySurface_ShouldLeaveItOut()
	{
		var surface = ZoneBuilder.Surface("Rue Léopold", 4);

		var result = AddressAssigner.Assign([Point(12, North(80))], [surface], null);

		result.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a corner address about as close to two different streets is left out.
	/// </summary>
	[Fact]
	public void Assign_AddressAsCloseToAnotherStreet_ShouldLeaveItOut()
	{
		var first = ZoneBuilder.Surface("Rue Léopold", 4, id: "1");
		var second = ZoneBuilder.Surface("Rue Albert", 4, id: "2");

		var result = AddressAssigner.Assign([Point(12, ZoneBuilder.Here)], [first, second], null);

		result.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a second surface of the same street does not make an address ambiguous.
	/// </summary>
	[Fact]
	public void Assign_TwoSurfacesOfTheSameStreet_ShouldKeepTheAddress()
	{
		var first = ZoneBuilder.Surface("Rue Léopold", 4, id: "1");
		var second = ZoneBuilder.Surface("Rue Léopold", 4, id: "2");

		var result = AddressAssigner.Assign([Point(12, ZoneBuilder.Here)], [first, second], null);

		result.Should().ContainSingle();
	}

	/// <summary>
	/// Verifies that a number far from all its neighbours on the street is left out as misplaced.
	/// </summary>
	[Fact]
	public void Assign_NumberFarFromAllItsNeighbours_ShouldLeaveItOut()
	{
		var surface = ZoneBuilder.Surface("Rue Léopold", 4);
		AddressPoint[] points = [Point(2, North(-6)), Point(4, North(-2)), Point(6, North(2)), Point(500, North(6))];

		var result = AddressAssigner.Assign(points, [surface], null);

		result.Select(address => address.House.Number).Should().Equal(2, 4, 6);
	}
	#endregion
}
