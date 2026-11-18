using Bunit;
using NSubstitute;
using Radzen.Blazor;
using ParkingTracker.Application.Features.Cards.Queries.ReadCard;
using ParkingTracker.Application.Features.Cards.Services;
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
///              settings store, shows the areas of a municipality with areas, has the scan button and fills the fields it found, and the card line warns before expiry.
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

		await PickDatesAsync(cut, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
		await cut.Find("#cardPlate").ChangeAsync("1-abc 234");
		await cut.Find("#cardNumber").ChangeAsync("B000-000-001");
		await TickAsync(cut, "#tolBox", 0);
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

		var municipalities = cut.FindComponent<RadzenDropDown<string>>();
		await cut.InvokeAsync(() => municipalities.Instance.ValueChanged.InvokeAsync("ixelles"));
		await cut.WaitForAssertionAsync(() => cut.FindAll("#maillesBox .rz-chkbox-box").Should().HaveCount(2));
		await TickAsync(cut, "#maillesBox", 1);
		await TickAsync(cut, "#limsBox", 0);
		await PickDatesAsync(cut, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
		await cut.Find("#cardForm").SubmitAsync();

		_ = Settings.Received(1).SaveCardAsync(Arg.Is<ParkingCard>(card => string.Equals(card.Municipality, "ixelles", StringComparison.Ordinal)
			&& card.Sector == null
			&& card.Mailles.Count == 1 && string.Equals(card.Mailles[0], "IXLR_2", StringComparison.Ordinal)
			&& card.Lims.Count == 1 && string.Equals(card.Lims[0], "LIM_X", StringComparison.Ordinal)));
	}

	/// <summary>
	/// Verifies that the dialog shows the scan button and an empty status line, and keeps its closing note.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_Open_ShouldShowTheScanButton()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		cut.Find("#scanBtn").TextContent.Should().Contain("Scan my card");
		cut.Find("#scanStatus").TextContent.Should().BeEmpty();
		cut.Markup.Should().Contain("Stored only on this phone.");
	}

	/// <summary>
	/// Verifies that the scan button fills the fields that were read and shows the line that asks the user to check them.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_FieldsRead_ShouldFillTheFormAndAskToCheck()
	{
		Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new ParsedCard("B012-345-678", "1ABC234", null, null, null, null, [])));
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		await cut.Find("#scanBtn").ClickAsync();

		cut.Find("#cardNumber").GetAttribute("value").Should().Be("B012-345-678");
		cut.Find("#cardPlate").GetAttribute("value").Should().Be("1ABC234");
		cut.Find("#scanStatus").TextContent.Should().Be("Filled: number, plate. Please double-check.");
	}
	#endregion

	#region Radzen Tests
	/// <summary>
	/// Verifies that the dialog is built from Radzen components and shows the fields of a municipality with a sector.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_Open_ShouldUseRadzenComponentsAndShowTheSectorFields()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		cut.FindComponents<RadzenDropDown<string>>().Should().HaveCount(2);
		cut.FindComponents<RadzenDatePicker<DateTime?>>().Should().HaveCount(2);
		cut.FindComponents<RadzenTextBox>().Should().HaveCount(2);
		cut.FindComponents<RadzenCheckBoxList<string>>().Should().HaveCount(1);
		cut.Find("#cardForm").Should().NotBeNull();
		cut.Find("#sectorBox").TextContent.Should().Contain("Sector (on your card)");
		cut.Find("#tolBox").TextContent.Should().Contain("Ixelles - Louise");
		cut.Markup.Should().Contain("Bruxelles (Ville) / Brussel (Stad)");
	}

	/// <summary>
	/// Verifies that clearing a date picker empties the date the view model holds.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Change_DateCleared_ShouldEmptyTheDateOfTheViewModel()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());
		await PickDatesAsync(cut, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
		var pickers = cut.FindComponents<RadzenDatePicker<DateTime?>>();

		await cut.InvokeAsync(() => pickers[0].Instance.ValueChanged.InvokeAsync(null));
		await cut.InvokeAsync(() => pickers[1].Instance.ValueChanged.InvokeAsync(null));

		Resolve<CardViewModel>().ValidFrom.Should().BeEmpty();
		Resolve<CardViewModel>().ValidTo.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that unticking a box removes its code from the selection of the view model.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Change_ToleranceUnticked_ShouldRemoveTheCodeFromTheSelection()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());
		await TickAsync(cut, "#tolBox", 0);
		Resolve<CardViewModel>().SelectedTolerances.Should().HaveCount(1);

		await TickAsync(cut, "#tolBox", 0);

		Resolve<CardViewModel>().SelectedTolerances.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a date of the view model that is not a date shows as an empty picker.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_DateNotADate_ShouldShowAnEmptyPicker()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		await cut.InvokeAsync(() => Resolve<CardViewModel>().ValidFrom = "not a date");
		await cut.InvokeAsync(() => Resolve<CardViewModel>().ValidTo = "2027-13-45");
		cut.Render();

		var pickers = cut.FindComponents<RadzenDatePicker<DateTime?>>();
		pickers[0].Instance.Value.Should().BeNull();
		pickers[1].Instance.Value.Should().BeNull();
	}

	/// <summary>
	/// Verifies that a date cleared after it was picked is refused with the visible form error.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_DateCleared_ShouldShowTheFormError()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());
		await PickDatesAsync(cut, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
		var pickers = cut.FindComponents<RadzenDatePicker<DateTime?>>();
		await cut.InvokeAsync(() => pickers[1].Instance.ValueChanged.InvokeAsync(null));

		await cut.Find("#cardForm").SubmitAsync();

		cut.Find("[role=alert]").TextContent.Should().Be("Enter the dates the card is valid from and until.");
		_ = Settings.DidNotReceiveWithAnyArgs().SaveCardAsync(default!);
	}

	/// <summary>
	/// Verifies that a municipality with areas hides the sector and the tolerances, and that one without areas shows only its note.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Change_MunicipalityWithAreas_ShouldHideTheSectorFields()
	{
		Sender.Send(Arg.Any<GetMaillesQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new GetMaillesResponse([new MailleOption("IXLR_1", "Ixelles 1")], [])));
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		var municipalities = cut.FindComponent<RadzenDropDown<string>>();
		await cut.InvokeAsync(() => municipalities.Instance.ValueChanged.InvokeAsync("ixelles"));

		await cut.WaitForAssertionAsync(() => cut.Find("#sectorBox").TextContent.Should().Contain("Tick every area"));
		cut.Find("#sectorBox").TextContent.Should().NotContain("Sector (on your card)");
		cut.Find("#tolBox").TextContent.Should().BeEmpty();
		cut.FindAll("#limsBox").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that the dates in reverse order are refused with the message of the card.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Submit_ReversedDates_ShouldStayOpenWithTheReason()
	{
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		await PickDatesAsync(cut, new DateOnly(2027, 1, 1), new DateOnly(2026, 1, 1));
		await cut.Find("#cardForm").SubmitAsync();

		cut.Find("[role=alert]").TextContent.Should().NotBeEmpty();
		cut.Find("#cardForm").Should().NotBeNull();
		_ = Settings.DidNotReceiveWithAnyArgs().SaveCardAsync(default!);
	}

	/// <summary>
	/// Verifies that the scan button is disabled and a progress circle shows while a card is being read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_Reading_ShouldDisableTheButtonAndShowProgress()
	{
		var release = new TaskCompletionSource<Result<ParsedCard>>();
		Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>()).Returns(_ => release.Task);
		var cut = Render<CardDialog>();
		await cut.InvokeAsync(() => Resolve<CardViewModel>().OpenAsync());

		var click = cut.Find("#scanBtn").ClickAsync();

		cut.Find("#scanBtn").HasAttribute("disabled").Should().BeTrue();
		cut.FindComponents<RadzenProgressBarCircular>().Should().HaveCount(1);
		release.SetResult(Result.Success(new ParsedCard(null, null, null, null, null, null, [])));
		await click;
		cut.FindComponents<RadzenProgressBarCircular>().Should().BeEmpty();
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

	#region Private Helpers
	/// <summary>
	/// Picks the two dates through the date pickers of the dialog.
	/// </summary>
	/// <param name="cut">The rendered dialog.</param>
	/// <param name="from">The date the card is valid from.</param>
	/// <param name="to">The date the card is valid until.</param>
	/// <returns>A task that completes when both dates were set.</returns>
	private static async Task PickDatesAsync(IRenderedComponent<CardDialog> cut, DateOnly from, DateOnly to)
	{
		var pickers = cut.FindComponents<RadzenDatePicker<DateTime?>>();
		await cut.InvokeAsync(() => pickers[0].Instance.ValueChanged.InvokeAsync(from.ToDateTime(TimeOnly.MinValue))).ConfigureAwait(true);
		await cut.InvokeAsync(() => pickers[1].Instance.ValueChanged.InvokeAsync(to.ToDateTime(TimeOnly.MinValue))).ConfigureAwait(true);
	}

	/// <summary>
	/// Ticks one box of a check-box list of the dialog.
	/// </summary>
	/// <param name="cut">The rendered dialog.</param>
	/// <param name="listSelector">The selector of the element that holds the list.</param>
	/// <param name="index">The position of the box in the list.</param>
	/// <returns>A task that completes when the box was clicked.</returns>
	private static Task TickAsync(IRenderedComponent<CardDialog> cut, string listSelector, int index) =>
		cut.FindAll(listSelector + " .rz-chkbox-box")[index].ClickAsync();
	#endregion
}
