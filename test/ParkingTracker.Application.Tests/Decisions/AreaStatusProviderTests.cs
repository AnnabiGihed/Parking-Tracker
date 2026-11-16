using NSubstitute;
using ParkingTracker.Application.Decisions;
using ParkingTracker.Application.Tests.TestDoubles;
using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Cards;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.ReferenceData;
using ParkingTracker.Domain.Zones;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="AreaStatusProvider"/>: a failure of the surfaces, the sector areas or the house numbers is
///              returned, missing rules are not a failure, and house numbers are only fetched when asked for.
/// </summary>
public sealed class AreaStatusProviderTests
{
	#region Test Infrastructure
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 11, 0, 0, TimeSpan.FromHours(1));

	private static readonly GeoBox Box = new(50.824, 4.358, 50.826, 4.362);

	private static readonly Error Down = new("Test.Down", "source down");

	private static readonly ParkingCard Card = new("test", null, new CardPeriod(null, null), [], [], ["LIM1"]);

	private static DecisionContext StreetsContext()
	{
		var municipality = new Municipality("bruxelles", "Bruxelles", "Brussel", "BRUXELLES", MunicipalityKind.Streets, [], [], null);
		var definition = new SectorDefinition("test", "Test", "Test", "bruxelles", null, [], [new SectorStreet("Rue Test", "Test straat", string.Empty, string.Empty, null, true)], []);
		return new DecisionContext(Card, municipality, SectorModel.Build(definition).Value, null);
	}

	private static DecisionContext WholeContext()
	{
		var municipality = new Municipality("test", "Testville", "Teststad", "TESTVILLE", MunicipalityKind.Whole, [], [], null);
		return new DecisionContext(Card, municipality, null, null);
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that failed surfaces are returned as the failure, without asking for anything else.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task LoadAsync_SurfacesFail_ShouldReturnTheFailure()
	{
		var sources = new SourceSubstitutes();
		sources.ParkingBrussels.GetZonesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<ZoneSurface>>(Down));

		var result = await sources.Provider.LoadAsync(WholeContext(), Box, Now, AddressFetching.Fetch, CancellationToken.None);

		result.Error.Should().Be(Down);
		await sources.Rules.DidNotReceiveWithAnyArgs().GetRulesAsync(default, default, default);
	}

	/// <summary>
	/// Verifies that failed sector areas are returned as the failure when the card needs them.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task LoadAsync_MaillesFailForACardWithBorderZones_ShouldReturnTheFailure()
	{
		var sources = new SourceSubstitutes();
		sources.ParkingBrussels.GetMaillesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<SectorMaille>>(Down));

		var result = await sources.Provider.LoadAsync(WholeContext(), Box, Now, AddressFetching.Fetch, CancellationToken.None);

		result.Error.Should().Be(Down);
	}

	/// <summary>
	/// Verifies that failed house numbers are returned as the failure when they were asked for.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task LoadAsync_AddressesFailWhenFetched_ShouldReturnTheFailure()
	{
		var sources = new SourceSubstitutes();
		sources.Addresses.GetAddressPointsAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<IReadOnlyList<AddressPoint>>(Down));

		var result = await sources.Provider.LoadAsync(StreetsContext(), Box, Now, AddressFetching.Fetch, CancellationToken.None);

		result.Error.Should().Be(Down);
	}

	/// <summary>
	/// Verifies that missing rules are not a failure: the surfaces are evaluated by their colour.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task LoadAsync_RulesFail_ShouldStillGiveTheSurfaces()
	{
		var sources = new SourceSubstitutes();
		sources.Rules.GetRulesAsync(Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
			.Returns(Result.Failure<IReadOnlyDictionary<string, ParkingRule>>(Down));

		var result = await sources.Provider.LoadAsync(WholeContext(), Box, Now, AddressFetching.Fetch, CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
	}
	#endregion

	#region Address Fetching Tests
	/// <summary>
	/// Verifies that house numbers are not fetched when the caller skips them.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task LoadAsync_SkipAddresses_ShouldNotAskForHouseNumbers()
	{
		var sources = new SourceSubstitutes();

		var result = await sources.Provider.LoadAsync(StreetsContext(), Box, Now, AddressFetching.Skip, CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		await sources.Addresses.DidNotReceiveWithAnyArgs().GetAddressPointsAsync(default, default);
	}

	/// <summary>
	/// Verifies that house numbers are fetched for a City of Brussels card when asked for.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task LoadAsync_FetchAddresses_ShouldAskForHouseNumbersOfTheUnpaddedArea()
	{
		var sources = new SourceSubstitutes();

		await sources.Provider.LoadAsync(StreetsContext(), Box, Now, AddressFetching.Fetch, CancellationToken.None);

		await sources.Addresses.Received(1).GetAddressPointsAsync(Box, Arg.Any<CancellationToken>());
	}
	#endregion
}
