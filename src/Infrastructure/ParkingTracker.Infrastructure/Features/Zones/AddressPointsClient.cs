using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the UrbIS address points over HTTP: one attempt, a limit of time, no cache.
/// </summary>
internal sealed class AddressPointsClient : IAddressPointsClient
{
	#region Fields
	/// <summary>
	/// HTTP client the source requests go through.
	/// </summary>
	private readonly HttpClient client;

	/// <summary>
	/// Time limits applied to each request.
	/// </summary>
	private readonly SourceTimeouts timeouts;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AddressPointsClient"/> class.
	/// </summary>
	/// <param name="client">The HTTP client of the service.</param>
	/// <param name="timeouts">The limits of time.</param>
	public AddressPointsClient(HttpClient client, SourceTimeouts timeouts)
	{
		this.client = client;
		this.timeouts = timeouts;
	}
	#endregion

	#region IAddressPointsClient Implementation
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<AddressPoint>>> GetAddressPointsAsync(GeoBox box, CancellationToken cancellationToken)
	{
		return SourceHttp.GetAsync(client, new SourceCall(SourceKind.Urbis, AddressPointParser.What, timeouts.Request), SourceRequests.AddressPoints(box), AddressPointParser.Parse, cancellationToken);
	}
	#endregion
}
