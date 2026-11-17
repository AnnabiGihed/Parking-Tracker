using NSubstitute;
using ParkingTracker.Application.Features.MapLayers.Queries.GetMapLayers;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.MapLayers.Queries.GetMapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="GetMapLayersQueryHandler"/>: only the switched-on layers are read, a failing layer does
///              not stop the others, and the outline of the card's areas is read only for cards that have areas.
/// </summary>
public sealed class GetMapLayersQueryHandlerTests
{
	#region Static Instances
	/// <summary>
	/// A municipality that issues cards per area.
	/// </summary>
	private static readonly Municipality Ixelles = new("ixelles", "Ixelles", "Elsene", "IXELLES", MunicipalityKind.Mailles, ["IXLR"], ["LIM_X"], null);
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Builds a handler over substitutes that answer with empty data.
	/// </summary>
	/// <param name="parking">The parking.brussels substitute.</param>
	/// <param name="mobility">The mobility substitute.</param>
	/// <param name="card">The saved card, or <see langword="null"/>.</param>
	/// <returns>The handler.</returns>
	private static GetMapLayersQueryHandler HandlerFor(IParkingBrusselsClient parking, IMobilityLayersClient mobility, ParkingCard? card = null)
	{
		parking.GetSpacesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<ReservedSpace>>([]));
		parking.GetMaillesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<SectorMaille>>([]));
		mobility.GetWorksAsync(Arg.Any<GeoBox>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<WorkItem>>([]));
		mobility.GetCarParksAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<CarPark>>([]));
		mobility.GetChargersAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<ChargingPoint>>([]));
		var settings = Substitute.For<ISettingsStore>();
		settings.GetCardAsync().Returns(card);
		var data = Substitute.For<IReferenceData>();
		data.GetReferenceData().Returns(Result.Success(new ReferenceDataSet([Ixelles], [], [])));
		return new GetMapLayersQueryHandler(parking, mobility, settings, data, new FixedClock());
	}

	/// <summary>
	/// Builds a query for a small area.
	/// </summary>
	/// <param name="layers">The layers to load.</param>
	/// <returns>The query.</returns>
	private static GetMapLayersQuery Query(LayerSettings layers) => new(50.8, 4.3, 50.9, 4.4, layers);
	#endregion

	#region Layer Tests
	/// <summary>
	/// Verifies that a layer that is switched off is not read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_OnlySpacesOn_ShouldNotAskTheOtherSources()
	{
		var parking = Substitute.For<IParkingBrusselsClient>();
		var mobility = Substitute.For<IMobilityLayersClient>();
		var handler = HandlerFor(parking, mobility);

		var result = await handler.Handle(Query(new LayerSettings(false, true, false, false, false)), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		await parking.Received(1).GetSpacesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>());
		await mobility.DidNotReceiveWithAnyArgs().GetWorksAsync(default, default, default);
		await mobility.DidNotReceiveWithAnyArgs().GetCarParksAsync(default, default);
		await mobility.DidNotReceiveWithAnyArgs().GetChargersAsync(default, default);
		await parking.DidNotReceiveWithAnyArgs().GetMaillesAsync(default, default);
	}

	/// <summary>
	/// Verifies that a failing layer is named in the failures while the other layers keep their data.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_CarParksFail_ShouldKeepChargersAndNameTheFailure()
	{
		var parking = Substitute.For<IParkingBrusselsClient>();
		var mobility = Substitute.For<IMobilityLayersClient>();
		var handler = HandlerFor(parking, mobility);
		mobility.GetCarParksAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<CarPark>>(new Error("Mobility.Unavailable", "car parks HTTP 500")));
		var charger = new ChargingPoint(new GeoPoint(50.85, 4.35), "Allego", "2", "22", null, null);
		mobility.GetChargersAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<ChargingPoint>>([charger]));

		var result = await handler.Handle(Query(new LayerSettings(false, false, false, true, true)), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.CarParks.Should().BeEmpty();
		result.Value.Chargers.Should().Equal(charger);
		result.Value.Failures.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>(GetMapLayersResponse.CarParksLayer, "car parks HTTP 500"));
	}
	#endregion

	#region Outline Tests
	/// <summary>
	/// Verifies that the outline holds only the areas and border zones that are on the card.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_CardWithAreas_ShouldKeepOnlyTheAreasOnTheCard()
	{
		var parking = Substitute.For<IParkingBrusselsClient>();
		var mobility = Substitute.For<IMobilityLayersClient>();
		var card = new ParkingCard("ixelles", null, new CardPeriod(null, null), [], ["IXLR_1"], ["LIM_X"]);
		var handler = HandlerFor(parking, mobility, card);
		static SectorMaille Area(string code) => new(code, null, new SurfaceShape([]));
		parking.GetMaillesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success<IReadOnlyList<SectorMaille>>([Area("IXLR_1"), Area("IXLR_2"), Area("LIM_X"), Area("OTHER")]));

		var result = await handler.Handle(Query(new LayerSettings(true, false, false, false, false)), CancellationToken.None);

		result.Value.MyAreas.Select(area => area.ZoneCode).Should().Equal("IXLR_1", "LIM_X");
	}

	/// <summary>
	/// Verifies that an area source that fails is named in the failures instead of being hidden.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_MaillesFail_ShouldNameTheAreasLayer()
	{
		var parking = Substitute.For<IParkingBrusselsClient>();
		var card = new ParkingCard("ixelles", null, new CardPeriod(null, null), [], ["IXLR_1"], []);
		var handler = HandlerFor(parking, Substitute.For<IMobilityLayersClient>(), card);
		parking.GetMaillesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>())
			.Returns(Result.Failure<IReadOnlyList<SectorMaille>>(new Error("ParkingBrussels.Unavailable", "sectors HTTP 500")));

		var result = await handler.Handle(Query(new LayerSettings(true, false, false, false, false)), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.MyAreas.Should().BeEmpty();
		result.Value.Failures.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>(GetMapLayersResponse.AreasLayer, "sectors HTTP 500"));
	}

	/// <summary>
	/// Verifies that a card without areas does not read the area layer, and that no card means no outline.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_NoAreasOnTheCard_ShouldNotAskForAreas()
	{
		var parking = Substitute.For<IParkingBrusselsClient>();
		var handler = HandlerFor(parking, Substitute.For<IMobilityLayersClient>(), new ParkingCard("ixelles", null, new CardPeriod(null, null), [], [], []));

		var result = await handler.Handle(Query(new LayerSettings(true, false, false, false, false)), CancellationToken.None);

		result.Value.MyAreas.Should().BeEmpty();
		await parking.DidNotReceiveWithAnyArgs().GetMaillesAsync(default, default);
	}
	#endregion
}
