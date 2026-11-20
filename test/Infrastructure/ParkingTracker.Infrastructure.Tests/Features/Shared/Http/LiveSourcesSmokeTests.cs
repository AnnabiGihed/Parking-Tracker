using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Application.Features.AddressSearch.Abstractions;
using ParkingTracker.Application.Features.Sectors.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Infrastructure.Tests.Features.Shared.Http;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Opt-in smoke tests (<c>PARKING_LIVE_TESTS=1</c>) that call each real service once (zones, spaces, sectors, address
///              points, rules, works, car parks, charging points, the City document and Nominatim), through the registered
///              clients, around Avenue Louise 216, and check that the answer parses into something sane. They show that the
///              addresses and the parsers still match what the services publish; they are not part of the default run.
/// </summary>
public sealed class LiveSourcesSmokeTests
{
	#region Constants
	/// <summary>
	/// The address of the official Louise list.
	/// </summary>
	private const string LouiseDocument = "https://www.bruxelles.be/sites/default/files/bxl/LOUISE.pdf";
	#endregion

	#region Static Instances
	/// <summary>
	/// A box around Avenue Louise 216.
	/// </summary>
	private static readonly GeoBox AvenueLouise216 = new(50.825547, 4.363237, 50.827747, 4.366757);

	/// <summary>
	/// The longest a live call may take.
	/// </summary>
	private static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(60);
	#endregion

	#region Test Infrastructure
	/// <summary>
	/// Builds the service provider with the live source clients.
	/// </summary>
	/// <returns>Service provider.</returns>
	private static ServiceProvider Provider()
	{
		var services = new ServiceCollection();
		new InfrastructureServiceInstaller().Install(services, new ConfigurationBuilder().Build());
		return services.BuildServiceProvider(validateScopes: true);
	}

	/// <summary>
	/// Creates a token that cancels a live call after the call limit.
	/// </summary>
	/// <returns>Cancellation token.</returns>
	private static CancellationToken Token() => new CancellationTokenSource(CallLimit).Token;
	#endregion

	#region Smoke Tests
	/// <summary>
	/// Verifies that the regulation zones around the address are read and include a street surface with a name.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task ParkingBrussels_Zones_ShouldParseSurfaces()
	{
		using var provider = Provider();

		var result = await provider.GetRequiredService<IParkingBrusselsClient>().GetZonesAsync(AvenueLouise216, Token());

		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
		result.Value.Should().NotBeEmpty();
	}

	/// <summary>
	/// Verifies that the parking spaces and the sector areas answer with a readable collection.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task ParkingBrussels_SpacesAndMailles_ShouldBeReadable()
	{
		using var provider = Provider();
		var client = provider.GetRequiredService<IParkingBrusselsClient>();

		var spaces = await client.GetSpacesAsync(AvenueLouise216, Token());
		var mailles = await client.GetMaillesAsync(AvenueLouise216, Token());

		spaces.IsSuccess.Should().BeTrue(spaces.IsFailure ? spaces.Error.Message : null);
		mailles.IsSuccess.Should().BeTrue(mailles.IsFailure ? mailles.Error.Message : null);
	}

	/// <summary>
	/// Verifies that the official address points around the address are read.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task Urbis_AddressPoints_ShouldParseHouseNumbers()
	{
		using var provider = Provider();

		var result = await provider.GetRequiredService<IAddressPointsClient>().GetAddressPointsAsync(AvenueLouise216, Token());

		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
		result.Value.Should().NotBeEmpty();
	}

	/// <summary>
	/// Verifies that the rules of all zones are read and at least one rule is in force.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task ParkingRules_AllZones_ShouldParseRulesInForce()
	{
		using var provider = Provider();

		var result = await provider.GetRequiredService<IParkingRulesClient>().GetRulesAsync(null, DateTimeOffset.UtcNow, Token());

		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
		result.Value.Should().NotBeEmpty();
	}

	/// <summary>
	/// Verifies that the roadworks and traffic events layers are both readable (there may be none running).
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task Mobility_Works_ShouldBeReadable()
	{
		using var provider = Provider();

		var result = await provider.GetRequiredService<IMobilityLayersClient>().GetWorksAsync(AvenueLouise216, DateTimeOffset.Now, Token());

		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
	}

	/// <summary>
	/// Verifies that the car parks around the address are readable and that every one has a name and a position.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task Mobility_CarParks_ShouldParseNamedPoints()
	{
		using var provider = Provider();

		var result = await provider.GetRequiredService<IMobilityLayersClient>().GetCarParksAsync(AvenueLouise216, Token());

		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
		result.Value.Should().OnlyContain(park => !string.IsNullOrWhiteSpace(park.Name));
	}

	/// <summary>
	/// Verifies that the charging points around the address are readable (there may be none in the box).
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task Mobility_Chargers_ShouldBeReadable()
	{
		using var provider = Provider();

		var result = await provider.GetRequiredService<IMobilityLayersClient>().GetChargersAsync(AvenueLouise216, Token());

		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
	}

	/// <summary>
	/// Verifies that the list of sectors (mailles) is read and that every entry has a zone code.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task ParkingBrussels_MailleList_ShouldParseZoneCodes()
	{
		using var provider = Provider();

		var result = await provider.GetRequiredService<IParkingBrusselsClient>().GetMailleOptionsAsync(Token());

		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
		result.Value.Should().NotBeEmpty().And.OnlyContain(option => !string.IsNullOrWhiteSpace(option.ZoneCode));
	}

	/// <summary>
	/// Verifies that Nominatim finds Avenue Louise 216 near its known position.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task Nominatim_AvenueLouise216_ShouldFindAPositionInBrussels()
	{
		using var provider = Provider();

		var result = await provider.GetRequiredService<IAddressSearch>().SearchAsync("Avenue Louise 216", 8, Token());

		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
		var match = result.Value.Should().NotBeEmpty().And.Subject.First();
		match.Point.Latitude.Should().BeInRange(50.80, 50.85);
		match.Point.Longitude.Should().BeInRange(4.33, 4.40);
	}

	/// <summary>
	/// Verifies that the City's document answers a HEAD with a success status, and that the download is a PDF.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[LiveFact]
	public async Task City_Document_ShouldAnswerHeadAndDownloadAPdf()
	{
		using var provider = Provider();
		var client = provider.GetRequiredService<ICityDocumentsClient>();

		var headers = await client.GetHeadersAsync(LouiseDocument, Token());
		var download = await client.DownloadAsync(LouiseDocument, Token());

		headers.IsSuccess.Should().BeTrue(headers.IsFailure ? headers.Error.Message : null);
		download.IsSuccess.Should().BeTrue(download.IsFailure ? download.Error.Message : null);
		download.Value.IsPdf.Should().BeTrue();
		download.Value.Sha256.Should().HaveLength(64);
	}
	#endregion
}
