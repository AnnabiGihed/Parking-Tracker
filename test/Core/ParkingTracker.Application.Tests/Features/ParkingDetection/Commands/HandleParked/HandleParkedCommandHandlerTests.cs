using NSubstitute;
using ParkingTracker.Application.Features.ParkingDetection.Abstractions;
using ParkingTracker.Application.Features.ParkingDetection.Commands.HandleParked;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;
using ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.ParkingDetection.Commands.HandleParked;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="HandleParkedCommandHandler"/>: the car is saved with the headline, one notification is sent
///              with the texts of the first version, the nearest free spot is added only when parking is not free in a known zone,
///              a failed decision still saves the car and says "can't verify", and with the app on screen nothing is sent.
/// </summary>
public sealed class HandleParkedCommandHandlerTests
{
	#region Static Instances
	/// <summary>
	/// Where the car stands in the tests.
	/// </summary>
	private static readonly GeoPoint Place = new(50.8265, 4.3650);
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : The handler over substitutes, with the clock of the tests, and the substitutes the assertions read.
	/// </summary>
	/// <param name="Handler">The handler.</param>
	/// <param name="Sender">The mediator substitute, which answers the decision and the search.</param>
	/// <param name="Settings">The settings substitute.</param>
	/// <param name="Notifier">The notifier substitute.</param>
	/// <param name="Visibility">The substitute that says whether the app is on screen; it says no.</param>
	private sealed record Setup(HandleParkedCommandHandler Handler, ISender Sender, ISettingsStore Settings, INotifier Notifier, IAppVisibility Visibility);

	/// <summary>
	/// Builds the handler and makes the decision query answer with the given decision.
	/// </summary>
	/// <param name="decision">The answer of the decision query.</param>
	/// <returns>The handler and its substitutes.</returns>
	private static Setup Build(SpotDecision decision)
	{
		var sender = Substitute.For<ISender>();
		sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(decision));
		sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<FreeSpot>>([]));
		var settings = Substitute.For<ISettingsStore>();
		var notifier = Substitute.For<INotifier>();
		var visibility = Substitute.For<IAppVisibility>();
		return new Setup(new HandleParkedCommandHandler(sender, settings, notifier, visibility, new FixedClock()), sender, settings, notifier, visibility);
	}

	/// <summary>
	/// Builds a decision in a known zone, so that the search for a free spot follows a paid answer.
	/// </summary>
	/// <param name="verdict">The verdict.</param>
	/// <param name="tariff">The tariff line, or <see langword="null"/>.</param>
	/// <returns>The decision.</returns>
	private static SpotDecision InZone(Verdict verdict, string? tariff)
	{
		var surface = new ZoneSurface("1", "Avenue Louise", "Louizalaan", "IXELLES", 1, null, new SurfaceShape([]));
		return new SpotDecision(verdict, "Detail.", [], tariff, new ZoneReading(surface, null, null), null);
	}
	#endregion

	#region Handle Tests
	/// <summary>
	/// Verifies that a free answer saves the car and notifies with the explanation alone, without a tariff or a search.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_FreeSpot_ShouldSaveTheCarAndNotifyWithoutSearching()
	{
		var setup = Build(InZone(Verdict.Free, "1 euro per hour"));

		var result = await setup.Handler.Handle(new HandleParkedCommand(Place.Latitude, Place.Longitude, 8), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().Be(new ParkedOutcome("Free for you here", ParkedDelivery.Notified));
		await setup.Settings.Received(1).SaveCarAsync(new ParkedCar(Place, new FixedClock().GetLocalNow(), "Free for you here", string.Empty));
		await setup.Notifier.Received(1).NotifyAsync("✅ Free for you here", "Detail.", Arg.Any<CancellationToken>());
		await setup.Sender.DidNotReceive().Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a paid answer in a zone adds the tariff and the nearest free spot, the distance rounded to whole metres.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_PaidSpotInZone_ShouldAddTariffAndNearestFreeSpot()
	{
		var setup = Build(InZone(Verdict.Pay, "1 euro per hour"));
		setup.Sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success<IReadOnlyList<FreeSpot>>([new FreeSpot(Place, 149.5, "Rue X", "IXELLES", "Rue X 12-20")]));

		await setup.Handler.Handle(new HandleParkedCommand(Place.Latitude, Place.Longitude, 8), CancellationToken.None);

		await setup.Notifier.Received(1).NotifyAsync(
			"⚠️ Paid parking",
			"Detail. · 1 euro per hour · Nearest free spot for you: Rue X 12-20, 150 m.",
			Arg.Any<CancellationToken>());
		await setup.Sender.Received(1).Send(Arg.Is<FindFreeSpotsQuery>(query => query.Count == 1), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a failed search leaves only the suggestion out.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_SearchFails_ShouldNotifyWithoutSuggestion()
	{
		var setup = Build(InZone(Verdict.Pay, null));
		setup.Sender.Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>())
			.Returns(Result.Failure<IReadOnlyList<FreeSpot>>(AreaQueryStubs.Down));

		var result = await setup.Handler.Handle(new HandleParkedCommand(Place.Latitude, Place.Longitude, 8), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		await setup.Notifier.Received(1).NotifyAsync("⚠️ Paid parking", "Detail.", Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that an uncertain answer without a zone gets the information icon and no search.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_CantVerify_ShouldUseInformationIconAndNotSearch()
	{
		var setup = Build(SpotDecision.CantVerify("No internet connection."));

		await setup.Handler.Handle(new HandleParkedCommand(Place.Latitude, Place.Longitude, null), CancellationToken.None);

		await setup.Notifier.Received(1).NotifyAsync("ℹ️ Can't verify right now", "No internet connection.", Arg.Any<CancellationToken>());
		await setup.Sender.DidNotReceive().Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies the accuracy that goes to the decision: the device value up to 25 m, 10 m when none or zero.
	/// </summary>
	/// <param name="reported">The accuracy of the fix, or null.</param>
	/// <param name="expected">The accuracy the decision is asked with.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(8.0, 8.0)]
	[InlineData(60.0, 25.0)]
	[InlineData(0.0, 10.0)]
	[InlineData(null, 10.0)]
	public async Task Handle_Accuracy_ShouldBeLimitedLikeTheFirstVersion(double? reported, double expected)
	{
		var setup = Build(SpotDecision.CantVerify("x"));

		await setup.Handler.Handle(new HandleParkedCommand(Place.Latitude, Place.Longitude, reported), CancellationToken.None);

		await setup.Sender.Received(1).Send(Arg.Is<DecideSpotQuery>(query => query.AccuracyMeters.Equals(expected)), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a failed decision still saves the car and sends the "can't verify" alert of the first version, and the
	/// command succeeds: the user is told that the spot could not be checked.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_DecisionFails_ShouldStillSaveTheCarAndNotifyCantVerify()
	{
		var setup = Build(SpotDecision.CantVerify("x"));
		setup.Sender.Send(Arg.Any<DecideSpotQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SpotDecision>(AreaQueryStubs.Down));

		var result = await setup.Handler.Handle(new HandleParkedCommand(Place.Latitude, Place.Longitude, 8), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.Headline.Should().Be("Can't verify right now");
		await setup.Settings.Received(1).SaveCarAsync(new ParkedCar(Place, new FixedClock().GetLocalNow(), "Can't verify right now", string.Empty));
		await setup.Notifier.Received(1).NotifyAsync("ℹ️ Can't verify right now", $"Unexpected error: {AreaQueryStubs.Down.Message}", Arg.Any<CancellationToken>());
		await setup.Sender.DidNotReceive().Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that with the app on screen the car is saved, no notification is sent and no free spot is searched, and that the
	/// outcome tells the caller to show the message and check the place itself.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_AppOnScreen_ShouldSaveTheCarAndSendNoNotification()
	{
		var setup = Build(InZone(Verdict.Pay, "1 euro per hour"));
		setup.Visibility.IsVisible.Returns(true);

		var result = await setup.Handler.Handle(new HandleParkedCommand(Place.Latitude, Place.Longitude, 8), CancellationToken.None);

		result.Value.Should().Be(new ParkedOutcome("Paid parking", ParkedDelivery.OnScreen));
		await setup.Settings.Received(1).SaveCarAsync(new ParkedCar(Place, new FixedClock().GetLocalNow(), "Paid parking", string.Empty));
		await setup.Notifier.DidNotReceiveWithAnyArgs().NotifyAsync(default!, default!, default);
		await setup.Sender.DidNotReceive().Send(Arg.Any<FindFreeSpotsQuery>(), Arg.Any<CancellationToken>());
	}
	#endregion
}
