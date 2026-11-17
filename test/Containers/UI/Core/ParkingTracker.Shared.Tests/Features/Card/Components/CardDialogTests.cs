using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.Sectors.Queries.GetMailles;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Shared.Features.Card.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.Card;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Shared.Tests.Features.Card.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for the card line, the card button and the card dialog: the dialog saves a typed card through the
///              settings store, shows the areas of a municipality with areas, has no scan section, and the card line warns before expiry.
/// </summary>
public sealed class CardDialogTests : UiTestContext
{
	#region Dialog Tests
	/// <summary>
	/// Verifies that the dialog is closed until the view model opens it.
	/// </summary>
	[Fact]
	public void Render_Closed_ShouldDrawNothing()
	{
		var cut = Render<CardDialog>();

		cut.FindAll("#cardForm").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that the typed card is saved through the settings store, with the plate in capitals, and that the dialog closes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_TypedCard_ShouldSaveThroughTheSettingsStore()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		await cut.Find("#cardFrom").ChangeAsync("2026-01-01");
		await cut.Find("#cardTo").ChangeAsync("2027-01-01");
		await cut.Find("#cardPlate").ChangeAsync("1-abc 234");
		await cut.Find("#cardNumber").ChangeAsync("B000-000-001");
		await cut.Find("input[name=tol]").ChangeAsync(true);
		await cut.Find("#cardForm").SubmitAsync();

		_ = Settings.Received(1).SaveCardAsync(Arg.Is<ParkingCard>(card => string.Equals(card.Municipality, "bruxelles", StringComparison.Ordinal)
			&& string.Equals(card.Sector, "louise", StringComparison.Ordinal)
			&& string.Equals(card.Plate, "1ABC234", StringComparison.Ordinal)
			&& string.Equals(card.Number, "B000-000-001", StringComparison.Ordinal)
			&& card.Tolerances.Count == 1
			&& card.Period == new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1))));
		cut.FindAll("#cardForm").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a form without dates is not saved and stays open with the reason.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_NoDates_ShouldStayOpenWithTheReason()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		await cut.Find("#cardForm").SubmitAsync();

		cut.Find("[role=alert]").TextContent.Should().Be("Enter the dates the card is valid from and until.");
		_ = Settings.DidNotReceiveWithAnyArgs().SaveCardAsync(default!);
	}

	/// <summary>
	/// Verifies that a municipality with areas loads them into a checklist and that a ticked area is saved with the card.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Change_MunicipalityWithAreas_ShouldListTheAreasAndSaveTheTickedOne()
	{
		Sender.Send(Arg.Any<GetMaillesQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new GetMaillesResponse([new MailleOption("IXLR_1", "Ixelles 1"), new MailleOption("IXLR_2", "Ixelles 2")], [new MailleOption("LIM_X", "Border")])));
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		await cut.Find("#muniSelect").ChangeAsync("ixelles");
		await cut.WaitForAssertionAsync(() => cut.FindAll("input[name=maille]").Should().HaveCount(2));
		await cut.FindAll("input[name=maille]")[1].ChangeAsync(true);
		await cut.Find("input[name=lim]").ChangeAsync(true);
		await cut.Find("#cardFrom").ChangeAsync("2026-01-01");
		await cut.Find("#cardTo").ChangeAsync("2027-01-01");
		await cut.Find("#cardForm").SubmitAsync();

		_ = Settings.Received(1).SaveCardAsync(Arg.Is<ParkingCard>(card => string.Equals(card.Municipality, "ixelles", StringComparison.Ordinal)
			&& card.Sector == null
			&& card.Mailles.Count == 1 && string.Equals(card.Mailles[0], "IXLR_2", StringComparison.Ordinal)
			&& card.Lims.Count == 1 && string.Equals(card.Lims[0], "LIM_X", StringComparison.Ordinal)));
	}

	/// <summary>
	/// Verifies that the dialog has no card photo scan, which is a later stage.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_Open_ShouldHaveNoScanSection()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		cut.FindAll(".scan, #scanInput").Should().BeEmpty();
		cut.Markup.Should().Contain("Stored only on this phone.");
	}
	#endregion

	#region Card Line Tests
	/// <summary>
	/// Verifies that without a card the line offers to add one and that the link opens the dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CardLine_NoCard_ShouldOfferToAddOneAndOpenTheDialog()
	{
		var cut = Render<CardLine>();

		cut.Find(".pill").TextContent.Should().Be("No card yet");
		await cut.Find("#addCardLink").ClickAsync();

		await Task.Yield();
		Resolve<CardViewModel>().IsOpen.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a card that expires within 30 days shows the warning and a renewal link that goes through the launcher.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CardLine_ExpiresSoon_ShouldWarnAndOpenTheRenewalEmail()
	{
		Settings.GetCardAsync().Returns(new ParkingCard("bruxelles", "louise", new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 27)), [], [], [], "B000-000-001", "1ABC234"));
		var cut = Render<CardLine>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().RefreshLineAsync());

		cut.Find(".warn").TextContent.Should().Contain("Expires in 10 day(s)");
		await cut.Find(".warn a").ClickAsync();

		await Launcher.Received(1).OpenAsync(new Uri("mailto:circulation.dem@brucity.be?subject=Renouvellement%20carte%20riverain%20B000-000-001"), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies the colour of the dot on the card button.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CardButton_Card_ShouldColourTheDot()
	{
		var cut = Render<CardButton>();
		cut.Find("#cardDot").ClassList.Should().Contain("none");

		Settings.GetCardAsync().Returns(new ParkingCard("bruxelles", "louise", new CardPeriod(null, new DateOnly(2027, 1, 1)), [], [], []));
		await cut.InvokeAsync(() => Resolve<CardViewModel>().RefreshLineAsync());

		cut.Find("#cardDot").ClassList.Should().NotContain("none").And.NotContain("bad");
	}
	#endregion
}
