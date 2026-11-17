using ParkingTracker.Application.Features.AddressSearch.Abstractions;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.AddressSearch;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Searches an address with the OpenStreetMap Nominatim service over HTTP, limited to Belgium and to the box of
///              the Brussels-Capital Region, one attempt with a limit of time. The request carries the typed text and the generic
///              product name only; the French answer language comes from the registration of the HTTP client.
/// </summary>
internal sealed class NominatimClient : IAddressSearch
{
	#region Fields
	/// <summary>
	/// HTTP client the requests go through.
	/// </summary>
	private readonly HttpClient client;

	/// <summary>
	/// Time limits applied to each request.
	/// </summary>
	private readonly SourceTimeouts timeouts;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="NominatimClient"/> class.
	/// </summary>
	/// <param name="client">The HTTP client of the service.</param>
	/// <param name="timeouts">The limits of time.</param>
	public NominatimClient(HttpClient client, SourceTimeouts timeouts)
	{
		this.client = client;
		this.timeouts = timeouts;
	}
	#endregion

	#region IAddressSearch Implementation
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<AddressMatch>>> SearchAsync(string text, CancellationToken cancellationToken)
	{
		var call = new SourceCall(SourceKind.Nominatim, AddressMatchParser.What, timeouts.Request);
		return SourceHttp.GetAsync(client, call, SourceRequests.AddressSearch(text), AddressMatchParser.Parse, cancellationToken);
	}
	#endregion
}
