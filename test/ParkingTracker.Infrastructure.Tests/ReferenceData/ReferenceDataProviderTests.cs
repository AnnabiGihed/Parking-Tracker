using ParkingTracker.Domain.ReferenceData;
using ParkingTracker.Infrastructure.ReferenceData;

namespace ParkingTracker.Infrastructure.Tests.ReferenceData;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tests that <see cref="ReferenceDataProvider"/> loads every embedded reference data file and that the
///              content equals the JavaScript data it replaces (counts, ids, kinds and spot-checked rows).
/// </summary>
public sealed class ReferenceDataProviderTests
{
	#region Test Infrastructure
	private static ReferenceDataSet Data()
	{
		var result = new ReferenceDataProvider().GetReferenceData();

		result.IsSuccess.Should().BeTrue();
		return result.Value;
	}

	private static SectorStreet Street(IEnumerable<SectorStreet> streets, string frenchName)
	{
		return streets.Single(street => street.FrenchName == frenchName);
	}
	#endregion

	#region Loading Tests
	/// <summary>
	/// Verifies that all three embedded files load.
	/// </summary>
	[Fact]
	public void GetReferenceData_EmbeddedFiles_ShouldLoad()
	{
		var result = new ReferenceDataProvider().GetReferenceData();

		result.IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that the data is loaded once and the same instance is served afterwards.
	/// </summary>
	[Fact]
	public void GetReferenceData_CalledTwice_ShouldReturnTheSameInstance()
	{
		var provider = new ReferenceDataProvider();

		provider.GetReferenceData().Value.Should().BeSameAs(provider.GetReferenceData().Value);
	}

	/// <summary>
	/// Verifies that an assembly without the embedded files gives a failure with the missing-resource code.
	/// </summary>
	[Fact]
	public void GetReferenceData_MissingResource_ShouldFailWithMissingCode()
	{
		var provider = new ReferenceDataProvider(typeof(ReferenceDataProviderTests).Assembly);

		var result = provider.GetReferenceData();

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("ReferenceData.ResourceMissing");
	}

	/// <summary>
	/// Verifies that a missing assembly is rejected.
	/// </summary>
	[Fact]
	public void Constructor_NullAssembly_ShouldThrowArgumentNullException()
	{
		var act = () => new ReferenceDataProvider(null!); // null on purpose: proves the guard clause rejects it

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region City Sector Tests
	/// <summary>
	/// Verifies the ten City sectors and their street counts against the JavaScript file.
	/// </summary>
	[Fact]
	public void CitySectors_ShouldHaveTheSameStreetCountsAsTheJavaScriptData()
	{
		var counts = Data().CitySectors.Select(sector => (sector.Id, sector.Streets.Count)).ToArray();

		counts.Should().Equal(
			("pentagone", 403),
			("nord", 35),
			("schuman", 104),
			("louise", 49),
			("roosevelt", 26),
			("bois-cambre", 5),
			("laeken", 154),
			("heysel", 26),
			("noh", 100),
			("haren", 62));
	}

	/// <summary>
	/// Verifies that each City sector belongs to the City of Brussels and carries a SHA-256 for its source.
	/// </summary>
	[Fact]
	public void CitySectors_ShouldBelongToBruxellesAndFingerprintTheirSource()
	{
		foreach (var sector in Data().CitySectors)
		{
			sector.Municipality.Should().Be("bruxelles");
			sector.Sources.Should().ContainSingle().Which.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
		}
	}

	/// <summary>
	/// Verifies that a row with a postcode keeps it and a row without keeps none.
	/// </summary>
	[Fact]
	public void CitySectors_Rows_ShouldKeepTheOptionalPostcode()
	{
		var streets = Data().CitySectors.SelectMany(sector => sector.Streets).ToArray();

		streets.Should().Contain(street => street.Postcode != null);
		streets.Should().Contain(street => street.Postcode == null);
	}
	#endregion

	#region Tolerance Sector Tests
	/// <summary>
	/// Verifies the Louise sector as spot-checked rows and fingerprinted sources.
	/// </summary>
	[Fact]
	public void ToleranceSectors_Louise_ShouldHaveItsStreetsSourcesAndArea()
	{
		var louise = Data().ToleranceSectors.Single();

		louise.Id.Should().Be("louise");
		louise.Streets.Should().HaveCount(49);
		louise.Sources.Should().HaveCount(2).And.OnlyContain(source => source.Sha256.Length == 64);
		louise.Area.Should().Be(new SectorArea(50.808, 4.345, 50.845, 4.392));
		var avenueLouise = Street(louise.Streets, "Avenue Louise");
		avenueLouise.DutchName.Should().Be("Louizalaan");
		avenueLouise.Odd.Should().Be("59 à fin");
		avenueLouise.Even.Should().Be("54 à fin");
	}

	/// <summary>
	/// Verifies the Ixelles-Louise tolerance agreement and a spot-checked row.
	/// </summary>
	[Fact]
	public void ToleranceSectors_Louise_ShouldCarryTheIxellesToleranceWith39Streets()
	{
		var tolerance = Data().ToleranceSectors.Single().Tolerances.Single();

		tolerance.Code.Should().Be("BXL_LIM_IXL_LOUISE");
		tolerance.Commune.Should().Be("Ixelles");
		tolerance.Streets.Should().HaveCount(39);
		var rueLens = Street(tolerance.Streets, "Rue Lens");
		rueLens.Odd.Should().Be("hors");
		rueLens.Even.Should().Be("2 à fin");
	}
	#endregion

	#region Municipality Tests
	/// <summary>
	/// Verifies that all 19 municipalities load with unique identifiers.
	/// </summary>
	[Fact]
	public void Municipalities_ShouldBeThe19OfTheRegion()
	{
		var municipalities = Data().Municipalities;

		municipalities.Should().HaveCount(19);
		municipalities.Select(municipality => municipality.Id).Should().OnlyHaveUniqueItems();
	}

	/// <summary>
	/// Verifies kind, zone prefixes and tolerance codes of each municipality against the JavaScript file.
	/// </summary>
	/// <param name="id">The municipality identifier.</param>
	/// <param name="kind">The expected kind.</param>
	/// <param name="prefixes">The expected zone prefixes, comma-separated.</param>
	/// <param name="lims">The expected tolerance codes, comma-separated.</param>
	[Theory]
	[InlineData("bruxelles", MunicipalityKind.Streets, "", "")]
	[InlineData("anderlecht", MunicipalityKind.Mailles, "ANDR", "")]
	[InlineData("auderghem", MunicipalityKind.Mailles, "AUDR,OUDR", "LIM_AUD_WABO")]
	[InlineData("berchem", MunicipalityKind.Mailles, "BSAR", "")]
	[InlineData("etterbeek", MunicipalityKind.Whole, "", "")]
	[InlineData("evere", MunicipalityKind.Mailles, "EVER", "")]
	[InlineData("forest", MunicipalityKind.Mailles, "FORR", "")]
	[InlineData("ganshoren", MunicipalityKind.Mailles, "GSHR", "")]
	[InlineData("ixelles", MunicipalityKind.Mailles, "IXLR", "LIM_IXL_BRU_ROOSEVELT")]
	[InlineData("jette", MunicipalityKind.Mailles, "JETR", "")]
	[InlineData("koekelberg", MunicipalityKind.Mailles, "KOEKR", "")]
	[InlineData("molenbeek", MunicipalityKind.Mailles, "SJMR", "")]
	[InlineData("saint-gilles", MunicipalityKind.Mailles, "STGR", "LIM_STG_BRU_POINCARE")]
	[InlineData("saint-josse", MunicipalityKind.Unknown, "", "")]
	[InlineData("schaerbeek", MunicipalityKind.Mailles, "SCHR", "")]
	[InlineData("uccle", MunicipalityKind.Unknown, "", "")]
	[InlineData("watermael", MunicipalityKind.Mailles, "WABOR", "LIM_AUD_WABO")]
	[InlineData("woluwe-lambert", MunicipalityKind.Wsl, "", "")]
	[InlineData("woluwe-pierre", MunicipalityKind.Mailles, "WSPP,WSPR", "")]
	public void Municipalities_Row_ShouldMatchTheJavaScriptData(string id, MunicipalityKind kind, string prefixes, string lims)
	{
		var municipality = Data().Municipalities.Single(candidate => candidate.Id == id);

		municipality.Kind.Should().Be(kind);
		municipality.Prefixes.Should().Equal(prefixes.Split(',', StringSplitOptions.RemoveEmptyEntries));
		municipality.Lims.Should().Equal(lims.Split(',', StringSplitOptions.RemoveEmptyEntries));
	}

	/// <summary>
	/// Verifies the names and the explanatory note of a municipality whose card cannot be checked.
	/// </summary>
	[Fact]
	public void Municipalities_Uccle_ShouldKeepNamesAndNote()
	{
		var uccle = Data().Municipalities.Single(municipality => municipality.Id == "uccle");

		uccle.Name.Should().Be("Uccle");
		uccle.NameNl.Should().Be("Ukkel");
		uccle.Mu.Should().Be("UCCLE");
		uccle.Note.Should().NotBeNullOrWhiteSpace();
	}
	#endregion
}
