using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.ViewModels.Features.Card;

namespace ParkingTracker.ViewModels.Tests.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CardLinePresenter"/>: the pill, the validity text, the expiry warning with the renewal
///              e-mail 30 days ahead, an expired or not yet valid card, a changed official list, and no card at all.
/// </summary>
public sealed class CardLinePresenterTests
{
	#region Static Instances
	/// <summary>
	/// Today in the tests.
	/// </summary>
	private static readonly DateOnly Today = new(2026, 11, 17);

	/// <summary>
	/// The City of Brussels, which issues cards per sector.
	/// </summary>
	private static readonly Municipality City = new("bruxelles", "Bruxelles (Ville)", "Brussel (Stad)", "BRUXELLES", MunicipalityKind.Streets, [], [], null);

	/// <summary>
	/// A municipality that issues cards per area.
	/// </summary>
	private static readonly Municipality Ixelles = new("ixelles", "Ixelles", "Elsene", "IXELLES", MunicipalityKind.Mailles, ["IXLR"], [], null);
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Builds a City card that is valid until a date.
	/// </summary>
	/// <param name="validTo">The last day.</param>
	/// <returns>The card.</returns>
	private static ParkingCard CityCard(DateOnly? validTo) => new("bruxelles", "louise", new CardPeriod(new DateOnly(2026, 1, 1), validTo), [], [], [], "B000-000-001", "1ABC234");
	#endregion

	#region Expiry Tests
	/// <summary>
	/// Verifies that thirty days before the end the line warns and offers the renewal e-mail with the card number in the subject.
	/// </summary>
	[Fact]
	public void Present_ThirtyDaysLeft_ShouldWarnAndOfferRenewal()
	{
		var line = CardLinePresenter.Present(CityCard(Today.AddDays(30)), City, "Louise", null, Today);

		line.Detail.Should().Be("Expires in 30 day(s)");
		line.DetailIsWarning.Should().BeTrue();
		line.RenewalLink!.AbsoluteUri.Should().Be("mailto:circulation.dem@brucity.be?subject=Renouvellement%20carte%20riverain%20B000-000-001");
		line.Dot.Should().Be(CardDot.Ok);
	}

	/// <summary>
	/// Verifies that thirty-one days before the end the line only says until when the card is valid.
	/// </summary>
	[Fact]
	public void Present_ThirtyOneDaysLeft_ShouldOnlyShowTheEndDate()
	{
		var line = CardLinePresenter.Present(CityCard(Today.AddDays(31)), City, "Louise", null, Today);

		line.Detail.Should().Be("Valid until 18/12/2026");
		line.DetailIsWarning.Should().BeFalse();
		line.RenewalLink.Should().BeNull();
	}

	/// <summary>
	/// Verifies that an expired card shows why, with a red dot.
	/// </summary>
	[Fact]
	public void Present_ExpiredCard_ShouldShowWhyWithRedDot()
	{
		var line = CardLinePresenter.Present(CityCard(Today.AddDays(-1)), City, "Louise", null, Today);

		line.Detail.Should().Be("Your card expired on 16/11/2026.");
		line.DetailIsWarning.Should().BeTrue();
		line.Dot.Should().Be(CardDot.Bad);
		line.RenewalLink.Should().BeNull();
	}
	#endregion

	#region Other Tests
	/// <summary>
	/// Verifies that a changed official list shows the warning and a red dot even for a valid card.
	/// </summary>
	[Fact]
	public void Present_OfficialListChanged_ShouldWarnWithRedDot()
	{
		var changed = OfficialListStatus.ChangedDocument("https://example.org/list.pdf", null, DateTimeOffset.UnixEpoch);

		var line = CardLinePresenter.Present(CityCard(Today.AddDays(200)), City, "Louise", changed, Today);

		line.Detail.Should().Be("City list changed — update the app");
		line.Dot.Should().Be(CardDot.Bad);
	}

	/// <summary>
	/// Verifies that without a card the line offers to add one, with an orange dot.
	/// </summary>
	[Fact]
	public void Present_NoCard_ShouldOfferToAddOne()
	{
		var line = CardLinePresenter.Present(null, null, null, null, Today);

		line.Pill.Should().Be("No card yet");
		line.ShowAddLink.Should().BeTrue();
		line.Dot.Should().Be(CardDot.None);
	}

	/// <summary>
	/// Verifies the pill: the sector, the Ixelles mark for tolerance codes and the plate; the number of areas for an area card.
	/// </summary>
	[Fact]
	public void Present_Pill_ShouldNameWhereTheCardIsValid()
	{
		var withTolerance = CityCard(Today.AddDays(100)) with { Tolerances = ["IXL_LOUISE"] };
		var areas = new ParkingCard("ixelles", null, new CardPeriod(null, null), [], ["IXLR_1", "IXLR_2"], []);
		var noAreas = areas with { Mailles = [] };

		CardLinePresenter.Present(withTolerance, City, "Louise", null, Today).Pill.Should().Be("Louise + Ixelles · 1ABC234");
		CardLinePresenter.Present(areas, Ixelles, null, null, Today).Pill.Should().Be("Ixelles · 2 areas");
		CardLinePresenter.Present(noAreas, Ixelles, null, null, Today).Pill.Should().Be("Ixelles · choose your areas");
	}
	#endregion
}
