using ParkingTracker.Application.Sources;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Zones;
using ParkingTracker.Infrastructure.Parsing;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the Brussels Mobility roadworks and traffic events over HTTP. The two layers are asked at the same time,
///              as the first version of the app did, and both must be readable.
/// </summary>
internal sealed class MobilityLayersClient : IMobilityLayersClient
{
	#region Fields
	private readonly HttpClient client;
	private readonly SourceTimeouts timeouts;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MobilityLayersClient"/> class.
	/// </summary>
	/// <param name="client">The HTTP client of the service.</param>
	/// <param name="timeouts">The limits of time.</param>
	public MobilityLayersClient(HttpClient client, SourceTimeouts timeouts)
	{
		this.client = client;
		this.timeouts = timeouts;
	}
	#endregion

	#region IMobilityLayersClient Implementation
	/// <inheritdoc />
	public async Task<Result<IReadOnlyList<WorkItem>>> GetWorksAsync(GeoBox box, DateTimeOffset now, CancellationToken cancellationToken)
	{
		var sitesCall = SourceHttp.GetAsync(client, new SourceCall(SourceKind.Mobility, WorkItemParser.RoadworksWhat, timeouts.Request), SourceRequests.Roadworks(box), json => WorkItemParser.ParseRoadworks(json, now), cancellationToken);
		var eventsCall = SourceHttp.GetAsync(client, new SourceCall(SourceKind.Mobility, WorkItemParser.EventsWhat, timeouts.Request), SourceRequests.TrafficEvents(box), json => WorkItemParser.ParseEvents(json, now), cancellationToken);
		var results = await Task.WhenAll(sitesCall, eventsCall).ConfigureAwait(false);
		var failed = results.FirstOrDefault(result => result.IsFailure);
		return failed is not null
			? Result.Failure<IReadOnlyList<WorkItem>>(failed.Error)
			: Result.Success<IReadOnlyList<WorkItem>>([.. results[0].Value, .. results[1].Value]);
	}
	#endregion
}
