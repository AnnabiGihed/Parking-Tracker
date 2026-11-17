using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Zones;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the parking.brussels WFS service over HTTP: one attempt per call, a limit of time, no cache. The request
///              addresses come from <see cref="SourceRequests"/> and the answers are read by the parsers.
/// </summary>
internal sealed class ParkingBrusselsClient : IParkingBrusselsClient
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
	/// Initializes a new instance of the <see cref="ParkingBrusselsClient"/> class.
	/// </summary>
	/// <param name="client">The HTTP client of the service.</param>
	/// <param name="timeouts">The limits of time.</param>
	public ParkingBrusselsClient(HttpClient client, SourceTimeouts timeouts)
	{
		this.client = client;
		this.timeouts = timeouts;
	}
	#endregion

	#region IParkingBrusselsClient Implementation
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<ZoneSurface>>> GetZonesAsync(GeoBox box, CancellationToken cancellationToken)
	{
		return SourceHttp.GetAsync(client, Call(ZoneSurfaceParser.What), SourceRequests.Zones(box), ZoneSurfaceParser.Parse, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<ReservedSpace>>> GetSpacesAsync(GeoBox box, CancellationToken cancellationToken)
	{
		return SourceHttp.GetAsync(client, Call(ReservedSpaceParser.What), SourceRequests.Spaces(box), ReservedSpaceParser.Parse, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<SectorMaille>>> GetMaillesAsync(GeoBox box, CancellationToken cancellationToken)
	{
		return SourceHttp.GetAsync(client, Call(SectorMailleParser.What), SourceRequests.Mailles(box), SectorMailleParser.Parse, cancellationToken);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Describes a call to parking.brussels with its timeout.
	/// </summary>
	/// <param name="what">Short description of the request, used in error messages.</param>
	/// <returns>The call description.</returns>
	private SourceCall Call(string what) => new(SourceKind.ParkingBrussels, what, timeouts.Request);
	#endregion
}
