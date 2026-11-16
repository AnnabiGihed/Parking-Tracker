using System.Text.Json;
using ParkingTracker.Application.Sources;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.Zones;
using ParkingTracker.Infrastructure.Parsing;
using ParkingTracker.Infrastructure.Sources;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Tests.Parity;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Implements every source port by replaying the responses <c>tools/parity/record.mjs</c> recorded from the real
///              services: it builds the request address with the production builders, looks the recorded answer up by method
///              and address, and reads it with the production parsers. A request that was not recorded fails the test, so the
///              C# code cannot ask for more than <c>live.js</c> did; <see cref="Requested"/> keeps the order and the repeats, so
///              a test can show it did not ask for less, for more or in another order.
/// </summary>
internal sealed class FixtureSources : IParkingBrusselsClient, IAddressPointsClient, IParkingRulesClient, IMobilityLayersClient, ICityDocumentsClient
{
	#region Fields
	private readonly Dictionary<string, RecordedResponse> responses = [];
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FixtureSources"/> class.
	/// </summary>
	/// <param name="scenarioDirectory">The directory of a recorded scenario.</param>
	public FixtureSources(string scenarioDirectory)
	{
		foreach (var file in Directory.EnumerateFiles(scenarioDirectory, "*.json").Where(path => Path.GetFileName(path) != "scenario.json"))
		{
			using var document = JsonDocument.Parse(File.ReadAllText(file));
			var root = document.RootElement;
			var key = KeyOf(root.GetProperty("method").GetString()!, root.GetProperty("url").GetString()!);
			responses[key] = RecordedResponse.From(root);
		}
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the keys (method and address) of the requests made so far, in order and with repeats.
	/// </summary>
	public List<string> Requested { get; } = [];
	#endregion

	#region IParkingBrusselsClient Implementation
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<ZoneSurface>>> GetZonesAsync(GeoBox box, CancellationToken cancellationToken)
	{
		return Task.FromResult(Get(SourceRequests.Zones(box)).Read(SourceKind.ParkingBrussels, ZoneSurfaceParser.What, ZoneSurfaceParser.Parse));
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<ReservedSpace>>> GetSpacesAsync(GeoBox box, CancellationToken cancellationToken)
	{
		return Task.FromResult(Get(SourceRequests.Spaces(box)).Read(SourceKind.ParkingBrussels, ReservedSpaceParser.What, ReservedSpaceParser.Parse));
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<SectorMaille>>> GetMaillesAsync(GeoBox box, CancellationToken cancellationToken)
	{
		return Task.FromResult(Get(SourceRequests.Mailles(box)).Read(SourceKind.ParkingBrussels, SectorMailleParser.What, SectorMailleParser.Parse));
	}
	#endregion

	#region IAddressPointsClient Implementation
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<AddressPoint>>> GetAddressPointsAsync(GeoBox box, CancellationToken cancellationToken)
	{
		return Task.FromResult(Get(SourceRequests.AddressPoints(box)).Read(SourceKind.Urbis, AddressPointParser.What, AddressPointParser.Parse));
	}
	#endregion

	#region IParkingRulesClient Implementation
	/// <inheritdoc />
	public Task<Result<IReadOnlyDictionary<string, ParkingRule>>> GetRulesAsync(string? code, DateTimeOffset now, CancellationToken cancellationToken)
	{
		var response = Get(SourceRequests.Rules(code));
		return Task.FromResult(response.Read(SourceKind.ParkingRules, ParkingRuleParser.What, json => ParkingRuleParser.Parse(json, now)));
	}
	#endregion

	#region IMobilityLayersClient Implementation
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<WorkItem>>> GetWorksAsync(GeoBox box, DateTimeOffset now, CancellationToken cancellationToken)
	{
		var sites = Get(SourceRequests.Roadworks(box)).Read(SourceKind.Mobility, WorkItemParser.RoadworksWhat, json => WorkItemParser.ParseRoadworks(json, now));
		if (sites.IsFailure)
		{
			return Task.FromResult(sites);
		}

		var events = Get(SourceRequests.TrafficEvents(box)).Read(SourceKind.Mobility, WorkItemParser.EventsWhat, json => WorkItemParser.ParseEvents(json, now));
		return Task.FromResult(events.IsFailure ? events : Result.Success<IReadOnlyList<WorkItem>>([.. sites.Value, .. events.Value]));
	}
	#endregion

	#region ICityDocumentsClient Implementation
	/// <inheritdoc />
	public Task<Result<DocumentHeaders>> GetHeadersAsync(string url, CancellationToken cancellationToken)
	{
		var response = GetRecorded(url, "HEAD");
		return Task.FromResult(Result.Success(new DocumentHeaders(response.Status, response.ETag, response.LastModified)));
	}

	/// <inheritdoc />
	public Task<Result<DownloadedDocument>> DownloadAsync(string url, CancellationToken cancellationToken)
	{
		var response = GetRecorded(url, "GET");
		var result = response.Status is >= 200 and < 300
			? Result.Success(new DownloadedDocument(response.IsPdf, response.BodySha256!))
			: Result.Failure<DownloadedDocument>(SourceFailures.HttpStatus(SourceKind.City, "bruxelles.be", response.Status));
		return Task.FromResult(result);
	}
	#endregion

	#region Private Helpers
	private static string KeyOf(string method, string url) => $"{method} {url}";

	private SourceResponse Get(string url) => GetRecorded(url, "GET").ToSourceResponse();

	private RecordedResponse GetRecorded(string url, string method)
	{
		var key = KeyOf(method, url);
		Requested.Add(key);
		return responses.TryGetValue(key, out var response)
			? response
			: throw new InvalidOperationException($"The request was not recorded by live.js: {key}");
	}
	#endregion
}
