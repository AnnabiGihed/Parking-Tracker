using NSubstitute;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.SpotDecisions.Queries.DecideSpot;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="DecideSpotQueryHandler"/>: a failed source becomes the answer "can't verify" and not a
///              failed result, offline and missing-card answers need no source, and a broken context is a failed result.
/// </summary>
public sealed class DecideSpotQueryHandlerTests
{
	#region Test Infrastructure
	/// <summary>
	/// Creates an Etterbeek decision context holding the given card.
	/// </summary>
	/// <param name="card">Saved parking card, if any.</param>
	/// <returns>Decision context.</returns>
	private static DecisionContext ContextWithCard(ParkingCard? card) => new(card, AreaQueryStubs.Etterbeek, null, null);

	/// <summary>
	/// Creates a saved card for Etterbeek that is valid without end date.
	/// </summary>
	/// <returns>Parking card.</returns>
	private static ParkingCard ValidCard() => new("etterbeek", null, new CardPeriod(null, null), [], [], []);

	/// <summary>
	/// Builds the handler over a context factory that returns the given context, source clients that are
	/// substitutes and a network status that is online or offline.
	/// </summary>
	/// <param name="context">The decision context.</param>
	/// <param name="online">Whether the device is online.</param>
	/// <returns>The built handler.</returns>
	private static (DecideSpotQueryHandler Handler, IParkingBrusselsClient ParkingBrussels) Handler(DecisionContext context, bool online = true)
	{
		var contexts = Substitute.For<IDecisionContextFactory>();
		contexts.CreateAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Result.Success(context));
		var parkingBrussels = Substitute.For<IParkingBrusselsClient>();
		var runner = new DecisionStepRunner(parkingBrussels, Substitute.For<IParkingRulesClient>(), Substitute.For<IAddressPointsClient>(), Substitute.For<IMobilityLayersClient>());
		var network = Substitute.For<INetworkStatus>();
		network.IsOnline.Returns(online);
		return (new DecideSpotQueryHandler(contexts, runner, network, new FixedClock()), parkingBrussels);
	}
	#endregion

	#region Handle Tests
	/// <summary>
	/// Verifies that an unreachable parking.brussels gives the answer "can't verify" with the reason, as a successful result.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ParkingBrusselsFails_ShouldAnswerCantVerifyNotFail()
	{
		var (handler, parkingBrussels) = Handler(ContextWithCard(ValidCard()));
		parkingBrussels.GetZonesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>())
			.Returns(Result.Failure<IReadOnlyList<ZoneSurface>>(new Error("ParkingBrussels.Unavailable", "parking.brussels HTTP 500")));

		var result = await handler.Handle(new DecideSpotQuery(50.825, 4.3606, 5), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.Verdict.Should().Be(Verdict.CantVerify);
		result.Value.Detail.Should().Be("Could not reach parking.brussels (parking.brussels HTTP 500).");
	}

	/// <summary>
	/// Verifies that being offline gives "can't verify" without asking any source.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_Offline_ShouldAnswerCantVerifyWithoutAskingASource()
	{
		var (handler, parkingBrussels) = Handler(ContextWithCard(ValidCard()), online: false);

		var result = await handler.Handle(new DecideSpotQuery(50.825, 4.3606, 5), CancellationToken.None);

		result.Value.Detail.Should().Be("No internet connection. The app only answers with live data.");
		await parkingBrussels.DidNotReceiveWithAnyArgs().GetZonesAsync(default, default);
	}

	/// <summary>
	/// Verifies that without a saved card the answer asks for one.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_NoCard_ShouldAskForTheCard()
	{
		var (handler, _) = Handler(ContextWithCard(null));

		var result = await handler.Handle(new DecideSpotQuery(50.825, 4.3606, 5), CancellationToken.None);

		result.Value.Verdict.Should().Be(Verdict.CantVerify);
		result.Value.Detail.Should().StartWith("Add your residents' parking card first");
	}

	/// <summary>
	/// Verifies that a context that cannot be built, for example broken reference data, is a failed result.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ContextFails_ShouldReturnTheFailure()
	{
		var contexts = Substitute.For<IDecisionContextFactory>();
		contexts.CreateAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
			.Returns(Result.Failure<DecisionContext>(new Error("ReferenceData.InvalidFormat", "broken")));
		var handler = new DecideSpotQueryHandler(contexts, Substitute.For<IDecisionStepRunner>(), Substitute.For<INetworkStatus>(), new FixedClock());

		var result = await handler.Handle(new DecideSpotQuery(50.825, 4.3606, 5), CancellationToken.None);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("ReferenceData.InvalidFormat");
	}
	#endregion
}
