using ParkingTracker.Application.Features.Cards.Services;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;

namespace ParkingTracker.Application.Tests.Features.Cards.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CardTextParser"/> with texts shaped like what text recognition returns for the City of
///              Brussels card, a card of another municipality and partial or noisy reads: the number with its misreadings, the
///              validity range, the plate, municipality and sector by French or Dutch name, and the tolerance codes.
/// </summary>
public sealed class CardTextParserTests
{
	#region Constants
	/// <summary>
	/// A full City of Brussels card as recognised from a photo.
	/// </summary>
	private const string BrusselsCard = """
		VILLE DE BRUXELLES
		STAD BRUSSEL
		CARTE DE STATIONNEMENT RIVERAINS
		N° B012-345-678
		Secteur : LOUISE
		Plaque 1ABC234
		Valable du 01/03/2026 - 28/02/2027
		BXL_LIM_IXL_LOUISE
		""";
	#endregion

	#region Static Instances
	/// <summary>
	/// The reference data the samples are read against.
	/// </summary>
	private static readonly ReferenceDataSet Data = BuildData();
	#endregion

	#region Brussels Card Tests
	/// <summary>
	/// Verifies that every field of a full City card is read.
	/// </summary>
	[Fact]
	public void Parse_FullBrusselsCard_ShouldReadEveryField()
	{
		var card = CardTextParser.Parse(BrusselsCard, Data);

		card.Number.Should().Be("B012-345-678");
		card.Plate.Should().Be("1ABC234");
		card.ValidFrom.Should().Be(new DateOnly(2026, 3, 1));
		card.ValidTo.Should().Be(new DateOnly(2027, 2, 28));
		card.MunicipalityId.Should().Be("bruxelles");
		card.SectorId.Should().Be("louise");
		card.Tolerances.Should().Equal("BXL_LIM_IXL_LOUISE");
		card.FoundFields.Should().Equal("number", "from", "to", "plate", "municipality", "sector", "tolerances");
	}

	/// <summary>
	/// Verifies that the Dutch name of a sector is found too.
	/// </summary>
	[Fact]
	public void Parse_DutchSectorName_ShouldFindTheSector()
	{
		var card = CardTextParser.Parse("STAD BRUSSEL sector Louiza", Data);

		card.SectorId.Should().Be("louise");
		card.MunicipalityId.Should().Be("bruxelles");
	}

	/// <summary>
	/// Verifies that the tolerance code is found when recognition broke it with spaces, dots or dashes and wrote it in lower case.
	/// </summary>
	/// <param name="text">The code as recognised.</param>
	[Theory]
	[InlineData("bxl lim ixl louise")]
	[InlineData("BXL-LIM-IXL.LOUISE")]
	[InlineData("BXL_LIM_IXL_LOUISE")]
	public void Parse_BrokenToleranceCode_ShouldStillFindIt(string text)
	{
		CardTextParser.Parse(text, Data).Tolerances.Should().Equal("BXL_LIM_IXL_LOUISE");
	}
	#endregion

	#region Number Tests
	/// <summary>
	/// Verifies how the misreadings of the card number are repaired.
	/// </summary>
	/// <param name="text">The number as recognised.</param>
	/// <param name="expected">The number the form gets.</param>
	[Theory]
	[InlineData("B012-345-678", "B012-345-678")]
	[InlineData("B012 - 345 - 678", "B012-345-678")]
	[InlineData("8012-345-678", "B012-345-678")]
	[InlineData("BO12-345-678", "B012-345-678")]
	[InlineData("B0O2-345-678", "B002-345-678")]
	[InlineData("B012–345—678", "B012-345-678")]
	public void Parse_NumberWithMisreadings_ShouldRepairIt(string text, string expected)
	{
		CardTextParser.Parse("N " + text + " valable", Data).Number.Should().Be(expected);
	}

	/// <summary>
	/// Verifies that text without a number, or with a number of the wrong shape, finds none.
	/// </summary>
	/// <param name="text">The recognised text.</param>
	[Theory]
	[InlineData("")]
	[InlineData("B012-345")]
	[InlineData("C012-345-678")]
	public void Parse_NoNumber_ShouldLeaveItOut(string text)
	{
		CardTextParser.Parse(text, Data).Number.Should().BeNull();
	}
	#endregion

	#region Period and Plate Tests
	/// <summary>
	/// Verifies that the validity range is read with the en dash that recognition often writes.
	/// </summary>
	[Fact]
	public void Parse_RangeWithEnDash_ShouldReadBothDates()
	{
		var card = CardTextParser.Parse("du 15/09/2025 – 14/09/2026", Data);

		card.ValidFrom.Should().Be(new DateOnly(2025, 9, 15));
		card.ValidTo.Should().Be(new DateOnly(2026, 9, 14));
	}

	/// <summary>
	/// Verifies that a range with a date that does not exist is ignored, and that a single date is not a range.
	/// </summary>
	/// <param name="text">The recognised text.</param>
	[Theory]
	[InlineData("31/02/2026 - 28/02/2027")]
	[InlineData("valable jusqu au 28/02/2027")]
	public void Parse_NoValidRange_ShouldLeaveTheDatesOut(string text)
	{
		var card = CardTextParser.Parse(text, Data);

		card.ValidFrom.Should().BeNull();
		card.ValidTo.Should().BeNull();
	}

	/// <summary>
	/// Verifies that the plate with a leading digit wins over one without, wherever they stand in the text.
	/// </summary>
	[Fact]
	public void Parse_TwoPlates_ShouldPreferTheOneWithALeadingDigit()
	{
		CardTextParser.Parse("ABC123 puis 2XYZ789", Data).Plate.Should().Be("2XYZ789");
	}

	/// <summary>
	/// Verifies that a plate without a leading digit is read.
	/// </summary>
	[Fact]
	public void Parse_OldPlateOnly_ShouldReadIt()
	{
		CardTextParser.Parse("plaque ABC123", Data).Plate.Should().Be("ABC123");
	}

	/// <summary>
	/// Verifies that lower case letters are not a plate.
	/// </summary>
	[Fact]
	public void Parse_LowerCasePlate_ShouldNotReadIt()
	{
		CardTextParser.Parse("plaque abc123", Data).Plate.Should().BeNull();
	}
	#endregion

	#region Municipality Tests
	/// <summary>
	/// Verifies that a municipality is found by its French or Dutch name and that the City of Brussels is not looked for by name.
	/// </summary>
	/// <param name="text">The recognised text.</param>
	/// <param name="expected">The identifier expected, or <see langword="null"/>.</param>
	[Theory]
	[InlineData("COMMUNE D IXELLES carte", "ixelles")]
	[InlineData("GEMEENTE ELSENE kaart", "ixelles")]
	[InlineData("ANDERLECHT", "anderlecht")]
	[InlineData("Bruxelles (Ville)", null)]
	[InlineData("rien de lisible", null)]
	public void Parse_MunicipalityName_ShouldFindTheMunicipality(string text, string? expected)
	{
		CardTextParser.Parse(text, Data).MunicipalityId.Should().Be(expected);
	}

	/// <summary>
	/// Verifies that a name is matched as a whole word.
	/// </summary>
	[Fact]
	public void Parse_NameInsideAnotherWord_ShouldNotMatch()
	{
		CardTextParser.Parse("NONIXELLESX", Data).MunicipalityId.Should().BeNull();
	}

	/// <summary>
	/// Verifies, as in the first version, that a name with a dash is found by the part before the dash.
	/// </summary>
	[Fact]
	public void Parse_NameWithDash_ShouldMatchItsFirstPart()
	{
		CardTextParser.Parse("Saint Josse", Data).MunicipalityId.Should().Be("saint-gilles");
	}
	#endregion

	#region Partial Read Tests
	/// <summary>
	/// Verifies that a text with only a number and a plate leaves everything else unset.
	/// </summary>
	[Fact]
	public void Parse_PartialRead_ShouldSetOnlyWhatWasFound()
	{
		var card = CardTextParser.Parse("B012-345-678\n1ABC234", Data);

		card.FoundFields.Should().Equal("number", "plate");
		card.MunicipalityId.Should().BeNull();
		card.Tolerances.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a text with nothing on it finds nothing.
	/// </summary>
	[Fact]
	public void Parse_NothingReadable_ShouldFindNothing()
	{
		CardTextParser.Parse("  \n lorem ipsum ", Data).FoundFields.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a missing text is rejected.
	/// </summary>
	[Fact]
	public void Parse_NullText_ShouldThrow()
	{
		var withoutText = () => CardTextParser.Parse(null!, Data);

		withoutText.Should().Throw<ArgumentNullException>();
	}

	/// <summary>
	/// Verifies that missing reference data is rejected.
	/// </summary>
	[Fact]
	public void Parse_NullData_ShouldThrow()
	{
		var withoutData = () => CardTextParser.Parse("x", null!);

		withoutData.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the reference data of the samples.
	/// </summary>
	/// <returns>The City of Brussels with one sector and its tolerance code, and three municipalities.</returns>
	private static ReferenceDataSet BuildData()
	{
		var city = new Municipality("bruxelles", "Bruxelles (Ville)", "Brussel (Stad)", "BRUXELLES", MunicipalityKind.Streets, [], [], null);
		var anderlecht = new Municipality("anderlecht", "Anderlecht", "Anderlecht", "ANDERLECHT", MunicipalityKind.Mailles, [], [], null);
		var ixelles = new Municipality("ixelles", "Ixelles", "Elsene", "IXELLES", MunicipalityKind.Mailles, [], [], null);
		var gilles = new Municipality("saint-gilles", "Saint-Gilles", "Sint-Gillis", "SAINT-GILLES", MunicipalityKind.Mailles, [], [], null);
		var louise = new SectorDefinition("louise", "Louise", "Louiza", null, null, [], [], []);
		var tolerance = louise with { Tolerances = [new ToleranceAgreement("BXL_LIM_IXL_LOUISE", "Ixelles - Louise", "ixelles", [])] };
		return new ReferenceDataSet([city, anderlecht, ixelles, gilles], [louise], [tolerance]);
	}
	#endregion
}
