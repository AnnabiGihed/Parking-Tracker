using NSubstitute;
using ParkingTracker.Application.Decisions;
using ParkingTracker.Application.Queries;
using ParkingTracker.Application.Tests.TestDoubles;
using ParkingTracker.Application.Sources;
using ParkingTracker.Domain.Cards;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.ReferenceData;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Queries;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="DecideSpotQueryHandler"/>: a failed source becomes the answer "can't verify" and not a
///              failed result, offline and missing-card answers need no source, and a broken context is a failed result.
/// </summary>
public sealed class DecideSpotQueryHandlerTests
{
	#region Test Infrastructure
	private static readonly Municipality Etterbeek = new("etterbeek", "Etterbeek", "Etterbeek", "ETTERBEEK", MunicipalityKind.Whole, [], [], null);

	private static DecisionContext ContextWithCard(ParkingCard? card) => new(card, Etterbeek, null, null);

	private static ParkingCard ValidCard() => new("etterbeek", null, new CardPeriod(null, null), [], [], []);

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
		parkingBrussels.GetZonesAsync(Arg.Any<ParkingTracker.Domain.Geometry.GeoBox>(), Arg.Any<CancellationToken>())
			.Returns(Result.Failure<IReadOnlyList<ParkingTracker.Domain.Zones.ZoneSurface>>(new Error("ParkingBrussels.Unavailable", "parking.brussels HTTP 500")));

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
