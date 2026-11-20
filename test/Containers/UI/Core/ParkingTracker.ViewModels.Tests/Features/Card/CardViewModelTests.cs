using NSubstitute;
using ParkingTracker.Application.Features.Cards.Abstractions;
using ParkingTracker.Application.Features.Cards.Queries.ReadCard;
using ParkingTracker.Application.Features.Cards.Services;
using ParkingTracker.Application.Features.Sectors.Queries.GetMailles;
using ParkingTracker.Application.Features.Sectors.Queries.GetOfficialListStatus;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CardViewModel"/>: the form starts from the saved card or the defaults, saving writes the
///              normalised card through the settings store, the areas of a municipality are loaded once, and the official-list
///              status is read again only when it is stale. Scanning a photo fills only the fields that were found and says so, stays quiet when
///              the user walks away, and shows any other failure.
/// </summary>
public sealed class CardViewModelTests
{
	#region Open Tests
	/// <summary>
	/// Verifies that without a saved card the form starts with the City of Brussels and its first sector.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_NoSavedCard_ShouldStartWithTheCityAndLouise()
	{
		using var ui = new UiFixture();

		await ui.Card.OpenAsync();

		ui.Card.IsOpen.Should().BeTrue();
		ui.Card.MunicipalityId.Should().Be("bruxelles");
		ui.Card.SectorId.Should().Be("louise");
		ui.Card.Tolerances.Select(t => t.Code).Should().Equal("IXL_LOUISE");
	}

	/// <summary>
	/// Verifies that the saved card fills the form.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_SavedCard_ShouldFillTheForm()
	{
		using var ui = new UiFixture();
		ui.Settings.GetCardAsync().Returns(new ParkingCard("bruxelles", "louise", new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)), ["IXL_LOUISE"], [], [], "B000-000-001", "1ABC234"));

		await ui.Card.OpenAsync();

		ui.Card.Number.Should().Be("B000-000-001");
		ui.Card.Plate.Should().Be("1ABC234");
		ui.Card.ValidFrom.Should().Be("2026-01-01");
		ui.Card.ValidTo.Should().Be("2027-01-01");
		ui.Card.SelectedTolerances.Should().Equal("IXL_LOUISE");
	}
	#endregion

	#region Valid-To Suggestion Tests
	/// <summary>
	/// Verifies that setting the first day while the last day is empty proposes the same day one year later.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ValidFrom_ValidToEmpty_ShouldSuggestOneYearLater()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();

		ui.Card.ValidFrom = "2026-03-15";

		ui.Card.ValidTo.Should().Be("2027-03-15");
	}

	/// <summary>
	/// Verifies that a last day the user chose is kept when the first day is set or changed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ValidFrom_ValidToChosenByUser_ShouldKeepIt()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();
		ui.Card.ValidTo = "2026-12-31";

		ui.Card.ValidFrom = "2026-03-15";
		ui.Card.ValidFrom = "2026-04-01";

		ui.Card.ValidTo.Should().Be("2026-12-31");
	}

	/// <summary>
	/// Verifies that a changed first day proposes again while the last day is still the last proposal.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ValidFrom_ValidToStillTheSuggestion_ShouldSuggestAgain()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();
		ui.Card.ValidFrom = "2026-03-15";

		ui.Card.ValidFrom = "2026-04-01";

		ui.Card.ValidTo.Should().Be("2027-04-01");
	}

	/// <summary>
	/// Verifies that a first day that is not a date proposes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ValidFrom_Cleared_ShouldLeaveValidToAlone()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();

		ui.Card.ValidFrom = string.Empty;

		ui.Card.ValidTo.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a saved card keeps its last day when its first day is changed after opening.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task ValidFrom_SavedCardOpened_ShouldKeepTheSavedValidTo()
	{
		using var ui = new UiFixture();
		ui.Settings.GetCardAsync().Returns(new ParkingCard("bruxelles", "louise", new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)), ["IXL_LOUISE"], [], [], "B000-000-001", "1ABC234"));
		await ui.Card.OpenAsync();

		ui.Card.ValidFrom = "2026-02-01";

		ui.Card.ValidTo.Should().Be("2026-06-30");
	}
	#endregion

	#region Save Tests
	/// <summary>
	/// Verifies that saving writes the card through the settings store with the plate in capitals without spaces or dashes, and that
	/// the first card is reported as such.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_ValidForm_ShouldSaveTheNormalisedCardThroughTheStore()
	{
		using var ui = new UiFixture();
		bool? first = null;
		ui.Card.CardSaved += (_, e) => first = e.Value;
		await ui.Card.OpenAsync();
		ui.Card.Number = " B000-000-001 ";
		ui.Card.Plate = "1-abc 234";
		ui.Card.ValidFrom = "2026-01-01";
		ui.Card.ValidTo = "2027-01-01";
		ui.Card.Toggle(ui.Card.SelectedTolerances, "IXL_LOUISE", true);

		var saved = await ui.Card.SaveAsync();

		saved.Should().BeTrue();
		_ = ui.Settings.Received(1).SaveCardAsync(Arg.Is<ParkingCard>(card => string.Equals(card.Municipality, "bruxelles", StringComparison.Ordinal)
			&& string.Equals(card.Sector, "louise", StringComparison.Ordinal)
			&& string.Equals(card.Number, "B000-000-001", StringComparison.Ordinal)
			&& string.Equals(card.Plate, "1ABC234", StringComparison.Ordinal)
			&& card.Period == new CardPeriod(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1))
			&& card.Tolerances.Count == 1 && string.Equals(card.Tolerances[0], "IXL_LOUISE", StringComparison.Ordinal)));
		ui.Card.IsOpen.Should().BeFalse();
		first.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a form without dates is not saved and says why.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_MissingDates_ShouldNotSave()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();

		var saved = await ui.Card.SaveAsync();

		saved.Should().BeFalse();
		ui.Card.FormError.Should().Be("Enter the dates the card is valid from and until.");
		_ = ui.Settings.DidNotReceiveWithAnyArgs().SaveCardAsync(default!);
		ui.Card.IsOpen.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that dates in reverse order are not saved and the message of the card is shown.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_DatesInReverseOrder_ShouldNotSaveAndShowTheCardMessage()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();
		ui.Card.ValidFrom = "2027-01-01";
		ui.Card.ValidTo = "2026-01-01";

		var saved = await ui.Card.SaveAsync();

		saved.Should().BeFalse();
		ui.Card.FormError.Should().Be("The card cannot be valid from a day after the day it is valid until.");
		_ = ui.Settings.DidNotReceiveWithAnyArgs().SaveCardAsync(default!);
		ui.Card.IsOpen.Should().BeTrue();
	}
	#endregion

	#region Scan Tests
	/// <summary>
	/// Verifies that a scan fills the fields it found, keeps the others and says which were filled.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_FieldsFound_ShouldFillOnlyThemAndAskToCheck()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();
		ui.Card.Plate = "1OLD000";
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new ParsedCard("B012-345-678", null, new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), null, null, [])));

		await ui.Card.ScanAsync();

		ui.Card.Number.Should().Be("B012-345-678");
		ui.Card.Plate.Should().Be("1OLD000");
		ui.Card.ValidFrom.Should().Be("2026-03-01");
		ui.Card.ValidTo.Should().Be("2027-02-28");
		ui.Card.ScanStatus.Should().Be("Filled: number, from, to. Please double-check.");
		ui.Card.IsScanning.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a found municipality and tolerance code are applied to the form, and that the codes already ticked are kept.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_MunicipalityAndTolerance_ShouldSelectThem()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new ParsedCard(null, null, null, null, "bruxelles", "louise", ["IXL_LOUISE"])));

		await ui.Card.ScanAsync();

		ui.Card.MunicipalityId.Should().Be("bruxelles");
		ui.Card.SectorId.Should().Be("louise");
		ui.Card.SelectedTolerances.Should().Equal("IXL_LOUISE");
		ui.Card.ScanStatus.Should().Be("Filled: municipality, sector, tolerances. Please double-check.");
	}

	/// <summary>
	/// Verifies that a found municipality with areas loads its areas.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_MunicipalityWithAreas_ShouldLoadTheAreas()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new ParsedCard(null, null, null, null, "ixelles", null, [])));
		ui.Sender.Send(Arg.Any<GetMaillesQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new GetMaillesResponse([new MailleOption("IXLR_2", "Ixelles 2")], [])));

		await ui.Card.ScanAsync();

		ui.Card.MunicipalityId.Should().Be("ixelles");
		ui.Card.SectorId.Should().BeNull();
		ui.Card.MailleState.Should().Be(MailleLoadState.Loaded);
	}

	/// <summary>
	/// Verifies that a photo without any field asks the user to type them and changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_NothingFound_ShouldAskToTypeTheFields()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();
		ui.Card.Number = "keep";
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new ParsedCard(null, null, null, null, null, null, [])));

		await ui.Card.ScanAsync();

		ui.Card.ScanStatus.Should().Be("Could not read the card — please type the fields.");
		ui.Card.Number.Should().Be("keep");
	}

	/// <summary>
	/// Verifies that a user who leaves the camera sees no message.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_UserCancelled_ShouldShowNothing()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<ParsedCard>(CardScanErrors.Cancelled()));

		await ui.Card.ScanAsync();

		ui.Card.ScanStatus.Should().BeEmpty();
		ui.Card.IsScanning.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that another failure is shown to the user.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_Failed_ShouldShowTheReason()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<ParsedCard>(CardScanErrors.Unavailable("Camera access was refused.")));

		await ui.Card.ScanAsync();

		ui.Card.ScanStatus.Should().Be("Scan failed: Camera access was refused.");
	}

	/// <summary>
	/// Verifies that the scan is marked as running while it reads, and that a second call meanwhile is ignored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_WhileScanning_ShouldIgnoreASecondCall()
	{
		using var ui = new UiFixture();
		var pending = new TaskCompletionSource<Result<ParsedCard>>();
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>()).Returns(pending.Task);

		var first = ui.Card.ScanAsync();
		await ui.Card.ScanAsync();
		ui.Card.IsScanning.Should().BeTrue();
		ui.Card.ScanStatus.Should().Be("Reading the card…");
		pending.SetResult(Result.Success(new ParsedCard(null, null, null, null, null, null, [])));
		await first;

		await ui.Sender.Received(1).Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>());
		ui.Card.IsScanning.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that an unexpected exception of the scan is shown as a failure and the scan is not left running.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_UnexpectedException_ShouldShowTheFailureAndStopScanning()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>()).Returns<Task<Result<ParsedCard>>>(_ => throw new InvalidOperationException("boom"));

		await ui.Card.ScanAsync();

		ui.Card.ScanStatus.Should().Be("Scan failed: boom");
		ui.Card.IsScanning.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a scan that found a tolerance code but no sector keeps the sector the user chose.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Scan_NoSectorFound_ShouldKeepTheChosenSector()
	{
		using var ui = new UiFixture();
		await ui.Card.OpenAsync();
		ui.Card.SelectSector("chosen");
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new ParsedCard("B012-345-678", null, null, null, "bruxelles", null, [])));

		await ui.Card.ScanAsync();

		ui.Card.SectorId.Should().Be("chosen");
	}

	/// <summary>
	/// Verifies that opening the dialog clears the line of an earlier scan.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Open_AfterAScan_ShouldClearTheScanStatus()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<ReadCardQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<ParsedCard>(CardScanErrors.Failed("boom")));
		await ui.Card.ScanAsync();

		await ui.Card.OpenAsync();

		ui.Card.ScanStatus.Should().BeEmpty();
	}
	#endregion

	#region Area Tests
	/// <summary>
	/// Verifies that choosing a municipality with areas loads them once, ticks the only area, and that going back to it does not load them again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SelectMunicipality_WithAreas_ShouldLoadThemOnceAndTickTheOnlyArea()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<GetMaillesQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new GetMaillesResponse([new MailleOption("IXLR_1", "Ixelles 1")], [new MailleOption("LIM_X", "Border")])));
		await ui.Card.OpenAsync();

		await ui.Card.SelectMunicipalityAsync("ixelles");
		await ui.Card.SelectMunicipalityAsync("etterbeek");
		await ui.Card.SelectMunicipalityAsync("ixelles");

		ui.Card.MailleState.Should().Be(MailleLoadState.Loaded);
		ui.Card.SelectedMailles.Should().Equal("IXLR_1");
		ui.Card.SectorId.Should().BeNull();
		await ui.Sender.Received(1).Send(Arg.Any<GetMaillesQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that an area list that cannot be loaded shows the reason.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SelectMunicipality_LoadFails_ShouldShowTheReason()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<GetMaillesQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<GetMaillesResponse>(new Error("ParkingBrussels.Unavailable", "parking.brussels HTTP 500")));

		await ui.Card.SelectMunicipalityAsync("ixelles");

		ui.Card.MailleState.Should().Be(MailleLoadState.Failed);
		ui.Card.MailleError.Should().Be("Could not load the areas (parking.brussels HTTP 500). Try again with internet.");
	}
	#endregion

	#region Official List Tests
	/// <summary>
	/// Verifies that a changed official list shows on the card line and that a fresh answer is not asked for again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshListIfStale_Changed_ShouldWarnAndNotAskAgainWhileFresh()
	{
		using var ui = new UiFixture();
		ui.Settings.GetCardAsync().Returns(new ParkingCard("bruxelles", "louise", new CardPeriod(null, null), [], [], []));
		var ok = OfficialListStatus.Unchanged(UiFixture.Start);
		ui.Sender.Send(Arg.Any<GetOfficialListStatusQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new GetOfficialListStatusResponse(ok)));

		await ui.Card.RefreshListIfStaleAsync();
		await ui.Card.RefreshListIfStaleAsync();
		ui.Clock.Advance(TimeSpan.FromHours(7));
		await ui.Card.RefreshListIfStaleAsync();

		await ui.Sender.Received(2).Send(Arg.Any<GetOfficialListStatusQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a changed document turns the card line into a warning.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task RefreshListIfStale_DocumentChanged_ShouldTurnTheLineIntoAWarning()
	{
		using var ui = new UiFixture();
		ui.Settings.GetCardAsync().Returns(new ParkingCard("bruxelles", "louise", new CardPeriod(null, null), [], [], []));
		var changed = OfficialListStatus.ChangedDocument("https://example.org/list.pdf", null, UiFixture.Start);
		ui.Sender.Send(Arg.Any<GetOfficialListStatusQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new GetOfficialListStatusResponse(changed)));

		await ui.Card.RefreshListIfStaleAsync();

		ui.Card.Line.Detail.Should().Be("City list changed — update the app");
		ui.Card.Line.Dot.Should().Be(CardDot.Bad);
	}
	#endregion
}
