using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SpotDecisions.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the official UrbIS address points (house numbers) published by Brussels Mobility.
/// </summary>
public interface IAddressPointsClient
{
	#region Methods
	/// <summary>
	/// Gets the address points in a box.
	/// </summary>
	/// <param name="box">The area, in degrees.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The address points, or a failure with an <c>Urbis.*</c> code and a message such as <c>UrbIS HTTP 500</c>.</returns>
	Task<Result<IReadOnlyList<AddressPoint>>> GetAddressPointsAsync(GeoBox box, CancellationToken cancellationToken);
	#endregion
}
