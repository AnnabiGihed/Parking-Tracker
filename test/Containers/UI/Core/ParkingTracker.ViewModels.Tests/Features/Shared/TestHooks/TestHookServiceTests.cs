using NSubstitute;
using ParkingTracker.Application.Features.Sectors.Queries.GetOfficialListStatus;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared.TestHooks;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="TestHookService"/>: the card goes through the settings store in the JSON of the first version,
///              decisions, free spots and the official-list check are the answers of the queries as plain data, a failed query is
///              thrown, and the platform status reports the permissions and the connection.
/// </summary>
public sealed class TestHookServiceTests
{
	#region Card Tests
	/// <summary>
	/// Verifies that a card in the old JSON is saved through the settings store.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SetCard_OldJson_ShouldSaveTheParsedCard()
	{
		using var ui = new UiFixture();
		var hooks = Create(ui);

		await hooks.SetCardAsync("""{"number":"TEST","plate":"1TST000","sector":"louise","from":"2026-01-01","to":"2099-12-31","tolerances":[]}""");

		_ = ui.Settings.Received(1).SaveCardAsync(Arg.Is<ParkingCard>(c => c.Municipality == "bruxelles" && c.Sector == "louise" && c.Number == "TEST"));
	}

	/// <summary>
	/// Verifies that a card saved through the hooks is announced like a card saved in the form, so the screen checks the spot again
	/// with it, and that it counts as the first card when none was saved before.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SetCard_NoCardBefore_ShouldAnnounceTheFirstCard()
	{
		using var ui = new UiFixture();
		var hooks = Create(ui);
		bool? announcedFirst = null;
		ui.Card.CardSaved += (_, e) => announcedFirst = e.Value;

		await hooks.SetCardAsync("""{"number":"TEST","plate":"1TST000","sector":"louise","from":"2026-01-01","to":"2099-12-31","tolerances":[]}""");

		announcedFirst.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that text that is not a card is rejected, and that nothing is saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task SetCard_NotACard_ShouldThrowAndSaveNothing()
	{
		using var ui = new UiFixture();
		var hooks = Create(ui);

		var act = () => hooks.SetCardAsync("nonsense");

		await act.Should().ThrowAsync<ArgumentException>();
		_ = ui.Settings.DidNotReceive().SaveCardAsync(Arg.Any<ParkingCard>());
	}

	/// <summary>
	/// Verifies that the saved card is read back in the JSON of the first version, and that no card gives <see langword="null"/>.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetCard_SavedCardOrNone_ShouldGiveJsonOrNull()
	{
		using var ui = new UiFixture();
		var hooks = Create(ui);
		(await hooks.GetCardAsync()).Should().BeNull();

		ui.Settings.GetCardAsync().Returns(new ParkingCard("bruxelles", "louise", new CardPeriod(null, null), [], [], []));

		(await hooks.GetCardAsync()).Should().Contain("\"municipality\":\"bruxelles\"").And.Contain("\"sector\":\"louise\"");
	}
	#endregion

	#region Query Tests
	/// <summary>
	/// Verifies that a decision is returned as status, title, detail, tariff and notes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task DecideAsync_Decision_ShouldReturnItsPlainFields()
	{
		using var ui = new UiFixture();
		ui.AnswerChecks(new SpotDecision(Verdict.Pay, "Paid here.", ["Roadworks"], "1 EUR/h", null, null));
		var hooks = Create(ui);

		var answer = await hooks.DecideAsync(50.8, 4.3, 5);

		answer.Should().Be(answer with { Status = "pay", Title = "PAY", Detail = "Paid here.", Tariff = "1 EUR/h" });
		answer.Notes.Should().Equal("Roadworks");
		await ui.Sender.Received(1).Send(Arg.Is<DecideSpotQuery>(q => Math.Abs((q.AccuracyMeters ?? 0) - 5) < 0.001 && Math.Abs(q.Latitude - 50.8) < 0.001), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a failed decision query is thrown.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task DecideAsync_FailedQuery_ShouldThrowWithTheMessage()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SpotDecision>(new Error("Decision.Invalid", "latitude out of range")));
		var hooks = Create(ui);

		var act = () => hooks.DecideAsync(500, 4, null);

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("latitude out of range");
	}

	/// <summary>
	/// Verifies that free spots are returned nearest first with label and distance, for the count asked.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FreeSpotsAsync_Spots_ShouldReturnLabelAndDistance()
	{
		using var ui = new UiFixture();
		ui.AnswerSearches(new FreeSpot(new GeoPoint(50.8252, 4.3608), 25, "Rue Test", "Ixelles", "Rue Test, Ixelles"));
		var hooks = Create(ui);

		var spots = await hooks.FreeSpotsAsync(50.8, 4.3, 3);

		spots.Should().Equal(new FreeSpotAnswer("Rue Test, Ixelles", 25, "Rue Test", "Ixelles"));
		await ui.Sender.Received(1).Send(Arg.Is<FindFreeSpotsQuery>(q => q.Count == 3), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a failed free-spot search is thrown with its message.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task FreeSpotsAsync_FailedQuery_ShouldThrowWithTheMessage()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<FreeSpot>>(new Error("Spots.Invalid", "no spots source")));
		var hooks = Create(ui);

		var act = () => hooks.FreeSpotsAsync(50.8, 4.3, 3);

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("no spots source");
	}

	/// <summary>
	/// Verifies that the official-list outcome is returned as a lower-case state, and as not checked when no check ran.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OfficialListAsync_StatusOrNone_ShouldReturnStateAndProblem()
	{
		using var ui = new UiFixture();
		var hooks = Create(ui);
		ui.Sender.Send(Arg.Any<GetOfficialListStatusQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new GetOfficialListStatusResponse(OfficialListStatus.Unchanged(UiFixture.Start))));
		(await hooks.OfficialListAsync()).Should().Be(new OfficialListAnswer("ok", null));

		ui.Sender.Send(Arg.Any<GetOfficialListStatusQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new GetOfficialListStatusResponse(null)));
		(await hooks.OfficialListAsync()).Should().Be(new OfficialListAnswer("notchecked", null));
	}

	/// <summary>
	/// Verifies that a failed official-list query is thrown with its message.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task OfficialListAsync_FailedQuery_ShouldThrowWithTheMessage()
	{
		using var ui = new UiFixture();
		ui.Sender.Send(Arg.Any<GetOfficialListStatusQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<GetOfficialListStatusResponse>(new Error("List.Unavailable", "list not readable")));
		var hooks = Create(ui);

		var act = () => hooks.OfficialListAsync();

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("list not readable");
	}
	#endregion

	#region Platform Tests
	/// <summary>
	/// Verifies that the permissions and the connection are reported in lower case.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task PlatformStatusAsync_ShouldReportPermissionsAndConnection()
	{
		using var ui = new UiFixture();
		ui.Permissions.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LocationPermissionStatus(PermissionState.Granted, PermissionState.Denied));
		var hooks = Create(ui);

		var status = await hooks.PlatformStatusAsync();

		status.Should().Be(new PlatformAnswer("granted", "denied", true));
	}
	#endregion

	#region Guard Tests
	/// <summary>
	/// Verifies that a missing dependency is rejected when the hooks are built.
	/// </summary>
	[Fact]
	public void Constructor_NullArguments_ShouldThrowArgumentNullException()
	{
		using var ui = new UiFixture();

		var noSender = () => new TestHookService(null!, ui.Settings, ui.Card, ui.Permissions, ui.Network); // null on purpose: proves the guard clause rejects it
		var noSettings = () => new TestHookService(ui.Sender, null!, ui.Card, ui.Permissions, ui.Network); // null on purpose: proves the guard clause rejects it
		var noCard = () => new TestHookService(ui.Sender, ui.Settings, null!, ui.Permissions, ui.Network); // null on purpose: proves the guard clause rejects it
		var noPermissions = () => new TestHookService(ui.Sender, ui.Settings, ui.Card, null!, ui.Network); // null on purpose: proves the guard clause rejects it
		var noNetwork = () => new TestHookService(ui.Sender, ui.Settings, ui.Card, ui.Permissions, null!); // null on purpose: proves the guard clause rejects it

		noSender.Should().Throw<ArgumentNullException>();
		noSettings.Should().Throw<ArgumentNullException>();
		noCard.Should().Throw<ArgumentNullException>();
		noPermissions.Should().Throw<ArgumentNullException>();
		noNetwork.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the hooks over the substitutes of the fixture.
	/// </summary>
	/// <param name="ui">The fixture.</param>
	/// <returns>The hooks.</returns>
	private static TestHookService Create(UiFixture ui) => new(ui.Sender, ui.Settings, ui.Card, ui.Permissions, ui.Network);
	#endregion
}
