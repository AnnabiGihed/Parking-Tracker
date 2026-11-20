using ParkingTracker.Application.Features.AddressSearch.Abstractions;
using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.AddressSearch;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Searches an address with the OpenStreetMap Nominatim service over HTTP, limited to Belgium and to the box of
///              the Brussels-Capital Region, one attempt with a limit of time, through the
///              <see cref="AddressSearchGate"/> that keeps the usage policy of the service. The request carries the typed text and the generic
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

	/// <summary>
	/// Spaces the requests out and answers a repeated question from memory.
	/// </summary>
	private readonly AddressSearchGate gate;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="NominatimClient"/> class.
	/// </summary>
	/// <param name="client">The HTTP client of the service.</param>
	/// <param name="timeouts">The limits of time.</param>
	/// <param name="gate">Spaces the requests out and answers a repeated question from memory.</param>
	public NominatimClient(HttpClient client, SourceTimeouts timeouts, AddressSearchGate gate)
	{
		this.client = client;
		this.timeouts = timeouts;
		this.gate = gate;
	}
	#endregion

	#region IAddressSearch Implementation
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<AddressMatch>>> SearchAsync(string text, int limit, CancellationToken cancellationToken)
	{
		var call = new SourceCall(SourceKind.Nominatim, AddressMatchParser.What, timeouts.Request);
		return gate.RunAsync(text, limit, () => SourceHttp.GetAsync(client, call, SourceRequests.AddressSearch(text, limit), AddressMatchParser.Parse, cancellationToken), cancellationToken);
	}
	#endregion
}
