using ParkingTracker.Domain.ReferenceData;
using ParkingTracker.Infrastructure.ReferenceData;

namespace ParkingTracker.Infrastructure.Tests.ReferenceData;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests that <see cref="ReferenceDataReader"/> parses valid text and reports malformed content as a
///              <c>ReferenceData.InvalidFormat</c> failure.
/// </summary>
public sealed class ReferenceDataReaderTests
{
	#region Test Infrastructure
	/// <summary>
	/// The failure code the reader uses for malformed content.
	/// </summary>
	private const string InvalidCode = "ReferenceData.InvalidFormat";
	#endregion

	#region Municipality Tests
	/// <summary>
	/// Verifies that a minimal municipality parses with empty optional lists.
	/// </summary>
	[Fact]
	public void ReadMunicipalities_MinimalEntry_ShouldParse()
	{
		var result = ReferenceDataReader.ReadMunicipalities("""[{"id":"x","name":"X","nameNl":"Xn","mu":"X","kind":"whole"}]""");

		result.IsSuccess.Should().BeTrue();
		var municipality = result.Value.Should().ContainSingle().Subject;
		municipality.Kind.Should().Be(MunicipalityKind.Whole);
		municipality.Prefixes.Should().BeEmpty();
		municipality.Lims.Should().BeEmpty();
		municipality.Note.Should().BeNull();
	}

	/// <summary>
	/// Verifies that an unknown kind is a failure.
	/// </summary>
	[Fact]
	public void ReadMunicipalities_UnknownKind_ShouldFail()
	{
		var result = ReferenceDataReader.ReadMunicipalities("""[{"id":"x","name":"X","nameNl":"Xn","mu":"X","kind":"circle"}]""");

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(InvalidCode);
	}

	/// <summary>
	/// Verifies that a numeric string is not accepted as a kind, only the named values are.
	/// </summary>
	[Fact]
	public void ReadMunicipalities_NumericKind_ShouldFail()
	{
		var result = ReferenceDataReader.ReadMunicipalities("""[{"id":"x","name":"X","nameNl":"Xn","mu":"X","kind":"0"}]""");

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(InvalidCode);
	}

	/// <summary>
	/// Verifies that text that is not JSON is a failure.
	/// </summary>
	[Fact]
	public void ReadMunicipalities_NotJson_ShouldFail()
	{
		var result = ReferenceDataReader.ReadMunicipalities("not json");

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(InvalidCode);
	}

	/// <summary>
	/// Verifies that a missing required property is a failure.
	/// </summary>
	[Fact]
	public void ReadMunicipalities_MissingProperty_ShouldFail()
	{
		var result = ReferenceDataReader.ReadMunicipalities("""[{"id":"x"}]""");

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(InvalidCode);
	}
	#endregion

	#region Sector Tests
	/// <summary>
	/// Verifies that a sector with a postcode row, an area and a tolerance parses.
	/// </summary>
	[Fact]
	public void ReadSectors_FullEntry_ShouldParse()
	{
		const string json = """
			{"s":{"id":"s","name":"S","nameNl":"Sn","municipality":"bruxelles","area":[1,2,3,4],
			"sources":[{"url":"u","etag":null,"length":5,"lastModified":null,"sha256":"h"}],
			"streets":[["Rue A","Straat A","","hors","1000"]],
			"tolerances":[{"code":"C","label":"L","commune":"Ixelles","streets":[["Rue B","Straat B","1-3","2"]]}]}}
			""";

		var result = ReferenceDataReader.ReadSectors(json, "test.json");

		result.IsSuccess.Should().BeTrue();
		var sector = result.Value.Should().ContainSingle().Subject;
		sector.Area.Should().Be(new SectorArea(1, 2, 3, 4));
		sector.Sources.Should().ContainSingle().Which.ETag.Should().BeNull();
		sector.Streets.Should().ContainSingle().Which.Postcode.Should().Be("1000");
		sector.Tolerances.Should().ContainSingle().Which.Streets.Should().ContainSingle().Which.Postcode.Should().BeNull();
	}

	/// <summary>
	/// Verifies that an empty cell means the whole side in a sector's street list.
	/// </summary>
	[Fact]
	public void ReadSectors_SectorStreets_ShouldReadEmptyAsWholeSide()
	{
		const string json = """{"s":{"id":"s","name":"S","nameNl":"Sn","streets":[["Rue A","Straat A","",""]]}}""";

		var result = ReferenceDataReader.ReadSectors(json, "test.json");

		result.Value.Should().ContainSingle().Which.Streets.Should().ContainSingle().Which.EmptyMeansWholeSide.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that an empty cell means not covered in a tolerance agreement.
	/// </summary>
	[Fact]
	public void ReadSectors_ToleranceStreets_ShouldReadEmptyAsNotCovered()
	{
		const string json = """
			{"s":{"id":"s","name":"S","nameNl":"Sn","streets":[],
			"tolerances":[{"code":"C","label":"L","commune":"Ixelles","streets":[["Rue B","Straat B","",""]]}]}}
			""";

		var result = ReferenceDataReader.ReadSectors(json, "test.json");

		result.Value.Should().ContainSingle().Which.Tolerances.Should().ContainSingle().Which.Streets.Should().ContainSingle()
			.Which.EmptyMeansWholeSide.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a street row with the wrong number of cells is a failure.
	/// </summary>
	[Fact]
	public void ReadSectors_ShortStreetRow_ShouldFail()
	{
		const string json = """{"s":{"id":"s","name":"S","nameNl":"Sn","streets":[["Rue A","Straat A"]]}}""";

		var result = ReferenceDataReader.ReadSectors(json, "test.json");

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(InvalidCode);
		result.Error.Message.Should().Contain("test.json");
	}

	/// <summary>
	/// Verifies that an area without four bounds is a failure.
	/// </summary>
	[Fact]
	public void ReadSectors_AreaWithThreeBounds_ShouldFail()
	{
		const string json = """{"s":{"id":"s","name":"S","nameNl":"Sn","area":[1,2,3],"streets":[]}}""";

		var result = ReferenceDataReader.ReadSectors(json, "test.json");

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(InvalidCode);
	}
	#endregion

	#region Missing Resource Tests
	/// <summary>
	/// Verifies the failure built for a resource that cannot be found.
	/// </summary>
	[Fact]
	public void Missing_ShouldNameTheResourceAndUseTheMissingCode()
	{
		var error = ReferenceDataReader.Missing("a.json");

		error.Code.Should().Be("ReferenceData.ResourceMissing");
		error.Message.Should().Contain("a.json");
	}
	#endregion
}
