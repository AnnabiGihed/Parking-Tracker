using NSubstitute;
using ParkingTracker.Application.Features.Sectors.Services;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.Services;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="DecisionContextFactory"/>: the municipality and sector the card selects, the fall-backs of
///              the first version of the app, and that only the City of Brussels checks the official documents.
/// </summary>
public sealed class DecisionContextFactoryTests
{
	#region Test Infrastructure
	/// <summary>
	/// The moment the tests run at.
	/// </summary>
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 11, 0, 0, TimeSpan.FromHours(1));

	/// <summary>
	/// The reference data the tests use.
	/// </summary>
	private static readonly ReferenceDataSet Data = new(
		[
			new Municipality("bruxelles", "Bruxelles (Ville)", "Brussel (Stad)", "BRUXELLES", MunicipalityKind.Streets, [], [], null),
			new Municipality("etterbeek", "Etterbeek", "Etterbeek", "ETTERBEEK", MunicipalityKind.Whole, [], [], null),
		],
		[
			new SectorDefinition("louise", "Louise", "Louiza", "BRUXELLES", null, [new SourceFingerprint("https://a.test/1.pdf", null, 1, null, "aa")], [new SectorStreet("Rue A", "A-straat", string.Empty, string.Empty, null, true)], []),
			new SectorDefinition("schuman", "Schuman", "Schuman", "BRUXELLES", null, [], [], []),
		],
		[
			new SectorDefinition("louise", "Louise", "Louiza", null, null, [new SourceFingerprint("https://a.test/1.pdf", null, 1, null, "aa"), new SourceFingerprint("https://a.test/2.pdf", null, 2, null, "bb")], [], []),
		]);

	/// <summary>
	/// Creates a saved card for the given municipality and sector.
	/// </summary>
	/// <param name="municipality">Municipality name of the card.</param>
	/// <param name="sector">Sector identifier of the card, if any.</param>
	/// <returns>Parking card.</returns>
	private static ParkingCard Card(string municipality, string? sector = null) => new(municipality, sector, new CardPeriod(null, null), [], [], []);

	/// <summary>
	/// Builds the factory over a settings store that holds the card, a reference data source that returns the data
	/// and a document checker that reports an unchanged list.
	/// </summary>
	/// <param name="card">Saved parking card, if any.</param>
	/// <param name="data">Reference data the stub returns, or a default set.</param>
	/// <returns>The built factory.</returns>
	private static (DecisionContextFactory Factory, IOfficialListChecker Checker) Factory(ParkingCard? card, Result<ReferenceDataSet>? data = null)
	{
		var settings = Substitute.For<ISettingsStore>();
		settings.GetCard().Returns(card);
		var referenceData = Substitute.For<IReferenceData>();
		referenceData.GetReferenceData().Returns(data ?? Result.Success(Data));
		var checker = Substitute.For<IOfficialListChecker>();
		checker.EnsureFreshAsync(Arg.Any<SectorModel>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
			.Returns(OfficialListStatus.Unchanged(Now));
		return (new DecisionContextFactory(settings, referenceData, checker), checker);
	}
	#endregion

	#region City of Brussels Tests
	/// <summary>
	/// Verifies that a City card gets the street lists of its sector, with the sources of the tolerance agreements merged, and the outcome of the document check.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CreateAsync_CityCard_ShouldBuildTheSectorAndCheckTheDocuments()
	{
		var (factory, checker) = Factory(Card("bruxelles", "louise"));

		var result = await factory.CreateAsync(Now, CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.Sector!.Definition.Sources.Select(source => source.Url).Should().Equal("https://a.test/1.pdf", "https://a.test/2.pdf");
		result.Value.OfficialList!.State.Should().Be(OfficialListState.Ok);
		await checker.Received(1).EnsureFreshAsync(Arg.Any<SectorModel>(), Now, Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that no card, or a card with an unknown sector, falls back to the City of Brussels and the Louise sector.
	/// </summary>
	/// <param name="sector">The sector on the card.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(null)]
	[InlineData("unknown")]
	public async Task CreateAsync_UnknownSector_ShouldFallBackToLouise(string? sector)
	{
		var (factory, _) = Factory(Card("bruxelles", sector));

		var result = await factory.CreateAsync(Now, CancellationToken.None);

		result.Value.Sector!.Definition.Id.Should().Be("louise");
	}

	/// <summary>
	/// Verifies that with no card the City of Brussels is the municipality.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CreateAsync_NoCard_ShouldUseTheCityOfBrussels()
	{
		var (factory, _) = Factory(null);

		var result = await factory.CreateAsync(Now, CancellationToken.None);

		result.Value.Card.Should().BeNull();
		result.Value.Municipality.Id.Should().Be("bruxelles");
	}
	#endregion

	#region Other Municipality Tests
	/// <summary>
	/// Verifies that a card of another municipality has no street lists and does not check the City documents.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CreateAsync_OtherMunicipality_ShouldNotCheckTheDocuments()
	{
		var (factory, checker) = Factory(Card("etterbeek"));

		var result = await factory.CreateAsync(Now, CancellationToken.None);

		result.Value.Sector.Should().BeNull();
		result.Value.OfficialList.Should().BeNull();
		await checker.DidNotReceiveWithAnyArgs().EnsureFreshAsync(default!, default, default);
	}

	/// <summary>
	/// Verifies that a card of an unknown municipality falls back to the City of Brussels.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CreateAsync_UnknownMunicipality_ShouldFallBackToTheCity()
	{
		var (factory, _) = Factory(Card("atlantis"));

		var result = await factory.CreateAsync(Now, CancellationToken.None);

		result.Value.Municipality.Id.Should().Be("bruxelles");
	}
	#endregion

	#region Failure Tests
	/// <summary>
	/// Verifies that broken reference data is returned as a failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CreateAsync_ReferenceDataFails_ShouldReturnTheFailure()
	{
		var (factory, _) = Factory(Card("bruxelles"), Result.Failure<ReferenceDataSet>(new Error("ReferenceData.InvalidFormat", "broken")));

		var result = await factory.CreateAsync(Now, CancellationToken.None);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("ReferenceData.InvalidFormat");
	}

	/// <summary>
	/// Verifies that reference data without the City of Brussels is a failure with its own code.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CreateAsync_NoCityInTheData_ShouldFail()
	{
		var withoutCity = new ReferenceDataSet([], [], []);
		var (factory, _) = Factory(null, Result.Success(withoutCity));

		var result = await factory.CreateAsync(Now, CancellationToken.None);

		result.Error.Code.Should().Be(DecisionContextFactory.MunicipalityMissingCode);
	}
	#endregion
}
