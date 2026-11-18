using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.ResidentCards.Aggregates;
using ParkingTracker.Domain.Features.ResidentCards.Errors;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Tests.Features.ResidentCards.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ResidentCard"/>: the card is only created when it fits its municipality (sector, areas,
///              border zones, dates), the number and plate are normalised, renewing keeps the dates valid, and the bridge to
///              <see cref="ParkingCard"/> gives back the values it was created from.
/// </summary>
public sealed class ResidentCardTests
{
	#region Static Instances
	/// <summary>
	/// A municipality with street lists, like the City of Brussels.
	/// </summary>
	private static readonly Municipality City = new("bruxelles", "Bruxelles (Ville)", "Brussel (Stad)", "BRUXELLES", MunicipalityKind.Streets, [], [], null);

	/// <summary>
	/// A municipality whose cards list areas and border zones.
	/// </summary>
	private static readonly Municipality Areas = new("ixelles", "Ixelles", "Elsene", "IXELLES", MunicipalityKind.Mailles, ["IXLR"], ["LIM_X"], null);

	/// <summary>
	/// A municipality whose card is valid everywhere in it.
	/// </summary>
	private static readonly Municipality Whole = new("etterbeek", "Etterbeek", "Etterbeek", "ETTERBEEK", MunicipalityKind.Whole, [], [], "No sectors.");

	/// <summary>
	/// A period that is valid in 2026.
	/// </summary>
	private static readonly CardPeriod Year = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Builds a card of the City with a sector and tolerance code.
	/// </summary>
	/// <returns>The card.</returns>
	private static ParkingCard CityCard() => new("bruxelles", "louise", Year, ["IXL_LOUISE"], [], [], "B000-000-001", "1ABC234");

	/// <summary>
	/// Builds a card of Ixelles with an area and a border zone.
	/// </summary>
	/// <returns>The card.</returns>
	private static ParkingCard AreasCard() => new("ixelles", null, Year, [], ["IXLR_1", "IXLR"], ["LIM_X"]);
	#endregion

	#region Create Tests
	/// <summary>
	/// Verifies that a valid City card is created and its bridge returns the same values.
	/// </summary>
	[Fact]
	public void Create_CityCard_ShouldRoundTripThroughParkingCard()
	{
		var result = ResidentCard.Create(City, CityCard());

		result.IsSuccess.Should().BeTrue();
		result.Value.ToParkingCard().Should().BeEquivalentTo(CityCard());
		result.Value.Id.Value.Should().NotBe(Guid.Empty);
	}

	/// <summary>
	/// Verifies that a valid card with areas and border zones keeps them in order.
	/// </summary>
	[Fact]
	public void Create_AreasCard_ShouldKeepAreasAndBorderZones()
	{
		var result = ResidentCard.Create(Areas, AreasCard());

		result.IsSuccess.Should().BeTrue();
		result.Value.Mailles.Should().Equal("IXLR_1", "IXLR");
		result.Value.Lims.Should().Equal("LIM_X");
		result.Value.ToParkingCard().Should().BeEquivalentTo(AreasCard());
	}

	/// <summary>
	/// Verifies that the number is trimmed and the plate put in capitals without spaces or dashes.
	/// </summary>
	[Fact]
	public void Create_TypedNumberAndPlate_ShouldNormaliseThem()
	{
		var typed = CityCard() with { Number = " B000-000-001 ", Plate = "1-abc 234" };

		var card = ResidentCard.Create(City, typed).Value.ToParkingCard();

		card.Number.Should().Be("B000-000-001");
		card.Plate.Should().Be("1ABC234");
	}

	/// <summary>
	/// Verifies that an empty number, plate or sector becomes absent.
	/// </summary>
	[Fact]
	public void Create_EmptyNumberPlateAndSector_ShouldBeAbsent()
	{
		var typed = CityCard() with { Sector = " ", Number = string.Empty, Plate = "- -" };

		var card = ResidentCard.Create(City, typed).Value;

		card.SectorCode.Should().BeNull();
		card.Number.Should().BeNull();
		card.Plate.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a card without dates is accepted, as a saved card of an older version may lack them.
	/// </summary>
	[Fact]
	public void Create_NoDates_ShouldSucceed()
	{
		var result = ResidentCard.Create(City, CityCard() with { Period = new CardPeriod(null, null) });

		result.IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a card valid for a single day is accepted.
	/// </summary>
	[Fact]
	public void Create_SameDayPeriod_ShouldSucceed()
	{
		var day = new DateOnly(2026, 5, 5);

		var result = ResidentCard.Create(City, CityCard() with { Period = new CardPeriod(day, day) });

		result.IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that dates in reverse order are refused.
	/// </summary>
	[Fact]
	public void Create_ValidFromAfterValidTo_ShouldFail()
	{
		var result = ResidentCard.Create(City, CityCard() with { Period = new CardPeriod(new DateOnly(2027, 1, 1), new DateOnly(2026, 1, 1)) });

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(ResidentCardErrors.PeriodReversed);
		result.ResultExceptionType.Should().Be(ResultExceptionType.ValidationError);
	}

	/// <summary>
	/// Verifies that a sector is refused for a municipality without street lists.
	/// </summary>
	[Fact]
	public void Create_SectorForMunicipalityWithoutStreetLists_ShouldFail()
	{
		var result = ResidentCard.Create(Whole, new ParkingCard("etterbeek", "louise", Year, [], [], []));

		result.Error.Should().Be(ResidentCardErrors.SectorNotAllowed);
	}

	/// <summary>
	/// Verifies that areas are refused for a municipality that does not issue cards per area.
	/// </summary>
	[Fact]
	public void Create_AreasForMunicipalityWithoutAreas_ShouldFail()
	{
		var result = ResidentCard.Create(City, CityCard() with { Mailles = ["IXLR_1"] });

		result.Error.Should().Be(ResidentCardErrors.MaillesNotAllowed);
	}

	/// <summary>
	/// Verifies that an area outside the prefixes of the municipality is refused, also when it only starts with the prefix letters.
	/// </summary>
	/// <param name="zoneCode">The zone code of the area.</param>
	[Theory]
	[InlineData("ETT_1")]
	[InlineData("IXLRX")]
	public void Create_AreaOutsideThePrefixes_ShouldFail(string zoneCode)
	{
		var result = ResidentCard.Create(Areas, AreasCard() with { Mailles = [zoneCode] });

		result.Error.Should().Be(ResidentCardErrors.MailleOutsideMunicipality(zoneCode));
	}

	/// <summary>
	/// Verifies that a border zone that the municipality does not list is refused.
	/// </summary>
	[Fact]
	public void Create_BorderZoneNotListed_ShouldFail()
	{
		var result = ResidentCard.Create(Areas, AreasCard() with { Lims = ["LIM_Y"] });

		result.Error.Should().Be(ResidentCardErrors.LimNotListed("LIM_Y"));
	}

	/// <summary>
	/// Verifies that a card naming another municipality than the one it is checked against is refused.
	/// </summary>
	[Fact]
	public void Create_OtherMunicipality_ShouldFail()
	{
		var result = ResidentCard.Create(Areas, CityCard());

		result.Error.Should().Be(ResidentCardErrors.MunicipalityMismatch);
	}

	/// <summary>
	/// Verifies that a blank municipality is refused.
	/// </summary>
	[Fact]
	public void Create_BlankMunicipality_ShouldFail()
	{
		var result = ResidentCard.Create(City, CityCard() with { Municipality = " " });

		result.Error.Should().Be(ResidentCardErrors.MunicipalityRequired);
	}

	/// <summary>
	/// Verifies that a missing argument is a programming error.
	/// </summary>
	[Fact]
	public void Create_NullArguments_ShouldThrow()
	{
		var withoutMunicipality = () => ResidentCard.Create(null!, CityCard());
		var withoutCard = () => ResidentCard.Create(City, null!);

		withoutMunicipality.Should().Throw<ArgumentNullException>();
		withoutCard.Should().Throw<ArgumentNullException>();
	}

	/// <summary>
	/// Verifies that every card gets its own identifier and that the aggregate raises no event.
	/// </summary>
	[Fact]
	public void Create_TwoCards_ShouldHaveDifferentIdsAndNoEvents()
	{
		var first = ResidentCard.Create(City, CityCard()).Value;
		var second = ResidentCard.Create(City, CityCard()).Value;

		first.Id.Should().NotBe(second.Id);
		first.GetDomainEvents().Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that changing the list the card was created from does not change the card.
	/// </summary>
	[Fact]
	public void Create_SourceListChangedAfterwards_ShouldNotChangeTheCard()
	{
		List<string> mailles = ["IXLR_1"];
		var card = ResidentCard.Create(Areas, AreasCard() with { Mailles = mailles }).Value;

		mailles.Add("IXLR_2");

		card.Mailles.Should().Equal("IXLR_1");
	}
	#endregion

	#region Renew Tests
	/// <summary>
	/// Verifies that renewing replaces the dates.
	/// </summary>
	[Fact]
	public void Renew_ValidPeriod_ShouldReplaceTheDates()
	{
		var card = ResidentCard.Create(City, CityCard()).Value;
		var next = new CardPeriod(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

		var result = card.Renew(next);

		result.IsSuccess.Should().BeTrue();
		card.Period.Should().Be(next);
	}

	/// <summary>
	/// Verifies that renewing with reversed dates fails and keeps the old dates.
	/// </summary>
	[Fact]
	public void Renew_ReversedPeriod_ShouldFailAndKeepTheDates()
	{
		var card = ResidentCard.Create(City, CityCard()).Value;

		var result = card.Renew(new CardPeriod(new DateOnly(2027, 1, 1), new DateOnly(2026, 1, 1)));

		result.Error.Should().Be(ResidentCardErrors.PeriodReversed);
		card.Period.Should().Be(Year);
	}
	#endregion

	#region Identifier Tests
	/// <summary>
	/// Verifies that an identifier is never empty and sorts like its value.
	/// </summary>
	[Fact]
	public void Id_Empty_ShouldBeRefusedAndOrderingFollowTheValue()
	{
		var empty = () => new ResidentCardId(Guid.Empty);
		var low = new ResidentCardId(new Guid(1, 0, 0, new byte[8]));
		var high = new ResidentCardId(new Guid(2, 0, 0, new byte[8]));

		empty.Should().Throw<ArgumentException>();
		(low < high).Should().BeTrue();
		(low <= high).Should().BeTrue();
		(high > low).Should().BeTrue();
		(high >= low).Should().BeTrue();
		(low > high).Should().BeFalse();
	}
	#endregion

	#region Restore Tests
	/// <summary>
	/// Verifies that a stored card comes back with its stored identifier and values, and raises no event.
	/// </summary>
	[Fact]
	public void Restore_StoredCard_ShouldKeepTheIdentifierAndRaiseNoEvent()
	{
		var id = ResidentCardId.New();

		var result = ResidentCard.Restore(id, City, CityCard());

		result.IsSuccess.Should().BeTrue();
		result.Value.Id.Should().Be(id);
		result.Value.ToParkingCard().Should().BeEquivalentTo(CityCard());
		result.Value.GetDomainEvents().Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a stored card is checked against its municipality like a new one.
	/// </summary>
	[Fact]
	public void Restore_CardThatBreaksARule_ShouldFail()
	{
		var reversed = CityCard() with { Period = new CardPeriod(new DateOnly(2027, 1, 1), new DateOnly(2026, 1, 1)) };

		ResidentCard.Restore(ResidentCardId.New(), City, reversed).Error.Should().Be(ResidentCardErrors.PeriodReversed);
		ResidentCard.Restore(ResidentCardId.New(), Areas, CityCard()).Error.Should().Be(ResidentCardErrors.MunicipalityMismatch);
	}

	/// <summary>
	/// Verifies that missing arguments are rejected.
	/// </summary>
	[Fact]
	public void Restore_NullArguments_ShouldThrow()
	{
		var noId = () => ResidentCard.Restore(null!, City, CityCard()); // null on purpose: proves the guard clause rejects it
		var noMunicipality = () => ResidentCard.Restore(ResidentCardId.New(), null!, CityCard()); // null on purpose: proves the guard clause rejects it
		var noCard = () => ResidentCard.Restore(ResidentCardId.New(), City, null!); // null on purpose: proves the guard clause rejects it

		noId.Should().Throw<ArgumentNullException>();
		noMunicipality.Should().Throw<ArgumentNullException>();
		noCard.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
