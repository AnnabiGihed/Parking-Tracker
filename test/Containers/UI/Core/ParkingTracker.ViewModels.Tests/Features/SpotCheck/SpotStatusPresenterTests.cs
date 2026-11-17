using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SpotStatusPresenter"/>: the headline, icon and colour of each verdict, when the tariff and
///              the actions show, and the chips.
/// </summary>
public sealed class SpotStatusPresenterTests
{
	#region Verdict Tests
	/// <summary>
	/// Verifies the headline, the icon and the colour key of each verdict.
	/// </summary>
	/// <param name="verdict">The verdict.</param>
	/// <param name="title">The expected headline.</param>
	/// <param name="icon">The expected icon.</param>
	/// <param name="colour">The expected colour key.</param>
	[Theory]
	[InlineData(Verdict.Free, "Free for you here", "check", "free")]
	[InlineData(Verdict.FreeNow, "Free for everyone right now", "check", "free")]
	[InlineData(Verdict.Pay, "Paid parking", "euro", "pay")]
	[InlineData(Verdict.Reserved, "Reserved space", "block", "pay")]
	[InlineData(Verdict.CheckNumber, "Check the house number", "warning", "unknown")]
	[InlineData(Verdict.TimeLimited, "Time-limited parking", "timer", "unknown")]
	[InlineData(Verdict.ParkingDisc, "Blue zone: use your disc", "timer", "unknown")]
	[InlineData(Verdict.CantVerify, "Can't verify right now", "help", "unknown")]
	[InlineData(Verdict.CheckCard, "CHECK YOUR CARD", "help", "unknown")]
	[InlineData(Verdict.CheckSigns, "CHECK THE SIGNS", "help", "unknown")]
	public void Present_Verdict_ShouldGiveHeadlineIconAndColour(Verdict verdict, string title, string icon, string colour)
	{
		var view = SpotStatusPresenter.Present(SpotDecision.Create(verdict, "explanation"));

		view.Title.Should().Be(title);
		view.IconName.Should().Be(icon);
		view.ColourKey.Should().Be(colour);
		view.Detail.Should().Be("explanation");
		view.IsIdle.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that the tariff shows for a paid answer and for "free now", but not for a spot that is free for the user.
	/// </summary>
	[Fact]
	public void Present_Tariff_ShouldShowForPayAndFreeNowOnly()
	{
		static SpotDecision With(Verdict verdict) => SpotDecision.Create(verdict, "x") with { Tariff = "€3 per hour" };

		SpotStatusPresenter.Present(With(Verdict.Pay)).Tariff.Should().Be("€3 per hour");
		SpotStatusPresenter.Present(With(Verdict.FreeNow)).Tariff.Should().Be("€3 per hour");
		SpotStatusPresenter.Present(With(Verdict.Free)).Tariff.Should().BeNull();
	}

	/// <summary>
	/// Verifies that "I parked here" is offered for a free spot, and the payment applications for a paid spot in a zone that is not a reserved space.
	/// </summary>
	[Fact]
	public void Present_Actions_ShouldOfferParkedHereWhenFreeAndPayAppsWhenPaid()
	{
		var reserved = UiFixture.InZone(Verdict.Pay) with { ReservedSpace = new ReservedSpace(new(50.8, 4.3), 5) };

		SpotStatusPresenter.Present(UiFixture.InZone(Verdict.Free)).ShowParkedHere.Should().BeTrue();
		SpotStatusPresenter.Present(UiFixture.InZone(Verdict.Pay)).ShowPayApps.Should().BeTrue();
		SpotStatusPresenter.Present(reserved).ShowPayApps.Should().BeFalse();
		SpotStatusPresenter.Present(SpotDecision.Create(Verdict.Pay, "x")).ShowPayApps.Should().BeFalse();
	}
	#endregion

	#region Chip Tests
	/// <summary>
	/// Verifies the chips of a live answer for the user's position: live time, position with accuracy rounded, and the zone.
	/// </summary>
	[Fact]
	public void Chips_LiveAnswerForMe_ShouldShowTimePositionAndZone()
	{
		var chips = SpotStatusPresenter.Chips(UiFixture.InZone(Verdict.Pay), CheckSource.Me, 12.5, UiFixture.Start);

		chips.Select(chip => chip.Text).Should().Equal("Live · 10:00:00", "My position ±13 m", "red zone · Ixelles");
		chips[0].Kind.Should().Be(ChipKind.Live);
		chips[2].ColourHex.Should().Be("#7f1d1d");
	}

	/// <summary>
	/// Verifies the chips of an answer without a zone for a pin.
	/// </summary>
	[Fact]
	public void Chips_AnswerWithoutZoneForPin_ShouldShowCheckedAndPin()
	{
		var chips = SpotStatusPresenter.Chips(SpotDecision.CantVerify("x"), CheckSource.Pin, null, UiFixture.Start);

		chips.Select(chip => chip.Text).Should().Equal("Checked · 10:00:00", "Pin");
		chips[0].Kind.Should().Be(ChipKind.Plain);
	}
	#endregion
}
