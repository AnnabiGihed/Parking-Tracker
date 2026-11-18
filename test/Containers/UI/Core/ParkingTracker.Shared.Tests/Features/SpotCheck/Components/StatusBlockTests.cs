using Bunit;
using NSubstitute;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Shared.Features.SpotCheck.Components;
using ParkingTracker.Shared.Tests.Features.Shared.TestDoubles;
using ParkingTracker.ViewModels.Features.SpotCheck;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Shared.Tests.Features.SpotCheck.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Component tests for <see cref="StatusBlock"/>, <see cref="LiveChips"/> and <see cref="NearestSpot"/>, built from Radzen: the status
///              block keeps the <c>.hero-title</c> and <c>.hero-sub</c> classes the emulator test reads, escapes the decision text, offers
///              the actions of the verdict, and the nearest spot opens the Waze link through the launcher.
/// </summary>
public sealed class StatusBlockTests : UiTestContext
{
	#region Status Block Tests
	/// <summary>
	/// Verifies the invitation shown before any check.
	/// </summary>
	[Fact]
	public void Render_BeforeAnyCheck_ShouldShowTheInvitationInTheHeroClasses()
	{
		var cut = Render<StatusBlock>();

		cut.Find(".hero-title").TextContent.Should().Be("Where do you want to park?");
		cut.Find(".hero-sub").TextContent.Should().Contain("my location");
		cut.Find("#status").ClassList.Should().Contain("idle");
	}

	/// <summary>
	/// Verifies that an answer fills <c>.hero-title</c> and <c>.hero-sub</c>, colours the block and shows the tariff and the notes.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_PaidAnswer_ShouldFillTheHeroClassesAndColourTheBlock()
	{
		AnswerChecks(SpotDecision.Create(Verdict.Pay, "Paid here, until 18:00.") with { Tariff = "€3 per hour", Notes = ["Roadworks nearby."] });
		AnswerSearches();
		var cut = Render<StatusBlock>();

		await cut.InvokeAsync(() => Resolve<SpotCheckViewModel>().HandleMapClickAsync(Here));

		cut.Find(".hero-title").TextContent.Should().Be("Paid parking");
		cut.Find(".hero-sub").TextContent.Should().Be("Paid here, until 18:00.");
		cut.Find("#status").ClassList.Should().Contain("pay");
		cut.Find(".tariff").TextContent.Should().Contain("€3 per hour");
		cut.Find(".notes li").TextContent.Should().Be("Roadworks nearby.");
	}

	/// <summary>
	/// Verifies that the decision text is shown as text, not as markup.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_DetailWithMarkup_ShouldEscapeIt()
	{
		AnswerChecks(SpotDecision.Create(Verdict.CantVerify, "<img src=x onerror=alert(1)>"));
		var cut = Render<StatusBlock>();

		await cut.InvokeAsync(() => Resolve<SpotCheckViewModel>().HandleMapClickAsync(Here));

		cut.Find(".hero-sub").InnerHtml.Should().Contain("&lt;img src=x onerror=alert(1)&gt;");
		cut.FindAll(".hero-sub img").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a paid spot in a zone offers the three payment applications and opens one through the launcher.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_PaidInZone_ShouldOfferPayAppsThroughTheLauncher()
	{
		AnswerChecks(InZone(Verdict.Pay));
		AnswerSearches();
		var cut = Render<StatusBlock>();
		await cut.InvokeAsync(() => Resolve<SpotCheckViewModel>().HandleMapClickAsync(Here));

		cut.FindAll(".paylinks button").Select(button => button.TextContent).Should().Equal("4411", "EasyPark", "Seety");
		await cut.FindAll(".paylinks button")[1].ClickAsync();

		await Launcher.Received(1).OpenAsync(new Uri("https://www.easypark.com"), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a free spot offers "I parked here", which saves the car and opens the car dialog.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_FreeSpot_ShouldOfferParkedHere()
	{
		AnswerChecks(InZone(Verdict.Free));
		var cut = Render<StatusBlock>();
		await cut.InvokeAsync(() => Resolve<SpotCheckViewModel>().HandleMapClickAsync(Here));

		await cut.Find(".actions .btn-primary").ClickAsync();

		_ = Settings.Received(1).SaveCarAsync(Arg.Any<ParkingTracker.Domain.Features.Cars.ValueObjects.ParkedCar>());
		Resolve<ParkingTracker.ViewModels.Features.Car.CarViewModel>().IsOpen.Should().BeTrue();
	}

	/// <summary>
	/// Verifies the title, the sub-text and the colour class of each kind of verdict, on the elements the emulator test reads.
	/// </summary>
	/// <param name="verdict">The verdict answered.</param>
	/// <param name="colour">The colour class expected on the block.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(Verdict.Free, "free")]
	[InlineData(Verdict.Pay, "pay")]
	[InlineData(Verdict.CantVerify, "unknown")]
	public async Task Render_Verdict_ShouldShowItsHeadlineDetailAndColour(Verdict verdict, string colour)
	{
		var decision = SpotDecision.Create(verdict, "The detail.");
		AnswerChecks(decision);
		AnswerSearches();
		var cut = Render<StatusBlock>();

		await cut.InvokeAsync(() => Resolve<SpotCheckViewModel>().HandleMapClickAsync(Here));

		cut.Find(".hero-title").TextContent.Should().Be(decision.Headline);
		cut.Find(".hero-sub").TextContent.Should().Be("The detail.");
		cut.Find("#status").ClassList.Should().Contain(colour);
	}

	/// <summary>
	/// Verifies that the block offers no action and no tariff before any check.
	/// </summary>
	[Fact]
	public void Render_BeforeAnyCheck_ShouldOfferNoActions()
	{
		var cut = Render<StatusBlock>();

		cut.FindAll(".actions, .tariff, .notes").Should().BeEmpty();
	}
	#endregion

	#region Chip Tests
	/// <summary>
	/// Verifies the chips: live time, position, and the coloured zone dot.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_LiveAnswer_ShouldShowTheChips()
	{
		AnswerChecks(InZone(Verdict.Pay));
		AnswerSearches();
		var cut = Render<LiveChips>();

		await cut.InvokeAsync(() => Resolve<SpotCheckViewModel>().HandleMapClickAsync(Here));

		cut.FindAll(".chip").Select(chip => chip.TextContent.Trim()).Should().Equal("Live · 10:00:00", "Pin", "red zone · Ixelles");
		cut.Find(".chip.live").Should().NotBeNull();
		cut.Find(".zone-chip i").GetAttribute("style").Should().Contain("#7f1d1d");
	}

	/// <summary>
	/// Verifies that the chips show nothing before any check.
	/// </summary>
	[Fact]
	public void Render_BeforeAnyCheck_ShouldShowNoChips()
	{
		var cut = Render<LiveChips>();

		cut.Find("#liveLine").Should().NotBeNull();
		cut.FindAll(".chip").Should().BeEmpty();
	}
	#endregion

	#region Nearest Spot Tests
	/// <summary>
	/// Verifies that the nearest spot shows its title and subtitle and that the Waze button opens the spot's link.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_SpotFound_ShouldOpenTheWazeLinkThroughTheLauncher()
	{
		AnswerSearches(new FreeSpot(new ParkingTracker.Domain.Features.Shared.ValueObjects.GeoPoint(50.8252, 4.3608), 25, "Rue Test", "Ixelles", "Rue Test, Ixelles"));
		var cut = Render<NearestSpot>();

		await cut.InvokeAsync(() => Resolve<NearestSpotViewModel>().ShowAsync(Here));

		cut.Find(".spot-title").TextContent.Should().Be("Rue Test, Ixelles");
		cut.Find(".spot-sub").TextContent.Should().Be("Free with your card · 25 m · about 1 min on foot");
		await cut.Find(".btn-waze").ClickAsync();
		await Launcher.Received(1).OpenAsync(new Uri("https://waze.com/ul?ll=50.825200,4.360800&navigate=yes"), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the Google Maps button opens the spot's link and that other spots are listed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_SeveralSpots_ShouldOpenGoogleMapsAndListTheOthers()
	{
		AnswerSearches(
			new FreeSpot(new ParkingTracker.Domain.Features.Shared.ValueObjects.GeoPoint(50.8252, 4.3608), 25, "Rue Test", "Ixelles", "Rue Test, Ixelles"),
			new FreeSpot(new ParkingTracker.Domain.Features.Shared.ValueObjects.GeoPoint(50.8260, 4.3610), 90, "Rue Autre", "Ixelles", "Rue Autre, Ixelles"));
		var cut = Render<NearestSpot>();

		await cut.InvokeAsync(() => Resolve<NearestSpotViewModel>().ShowAsync(Here));
		await cut.Find(".btn-gmaps").ClickAsync();

		cut.Find(".spot-more").TextContent.Should().Contain("Rue Autre, Ixelles");
		await Launcher.Received(1).OpenAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies the message when no spot is covered.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_NoSpotFound_ShouldSayNoSpotIsCovered()
	{
		AnswerSearches();
		var cut = Render<NearestSpot>();

		await cut.InvokeAsync(() => Resolve<NearestSpotViewModel>().ShowAsync(Here));

		cut.Find("#nearest").ClassList.Should().NotContain("hidden");
		cut.Find(".spot-loading").TextContent.Should().Be("No spot covered by your card within ~400 m right now.");
		cut.FindAll(".nav-row").Should().BeEmpty();
	}

	/// <summary>
	/// Verifies the message when the search fails.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Render_SearchFailed_ShouldShowTheFailureMessage()
	{
		Sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<FreeSpot>>(new Error("Search.Unavailable", "offline")));
		var cut = Render<NearestSpot>();

		await cut.InvokeAsync(() => Resolve<NearestSpotViewModel>().ShowAsync(Here));

		cut.Find(".spot-loading").TextContent.Should().Be("Could not search right now (offline).");
	}

	/// <summary>
	/// Verifies that the block is hidden until a search runs.
	/// </summary>
	[Fact]
	public void Render_NoSearch_ShouldBeHidden()
	{
		var cut = Render<NearestSpot>();

		cut.Find("#nearest").ClassList.Should().Contain("hidden");
	}
	#endregion
}
