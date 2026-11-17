using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SpotDecisions.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the Brussels Mobility layers: roadworks (Osiris) and traffic events that disturb parking, and the public
///              car parks and charging points the map layers show.
/// </summary>
public interface IMobilityLayersClient
{
	#region Methods
	/// <summary>
	/// Gets the roadworks and traffic events in a box that are running at a moment. Both layers must be readable.
	/// </summary>
	/// <param name="box">The area, in degrees.</param>
	/// <param name="now">The moment on the clock of the device; items that ended or have not started are left out.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The items, or a failure with a <c>Mobility.*</c> code and a message such as <c>roadworks HTTP 500</c>.</returns>
	Task<Result<IReadOnlyList<WorkItem>>> GetWorksAsync(GeoBox box, DateTimeOffset now, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the public car parks in a box. A car park the service marks as not active is left out.
	/// </summary>
	/// <param name="box">The area, in degrees.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The car parks, or a failure with a <c>Mobility.*</c> code and a message such as <c>car parks HTTP 500</c>.</returns>
	Task<Result<IReadOnlyList<CarPark>>> GetCarParksAsync(GeoBox box, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the charging points for electric vehicles in a box.
	/// </summary>
	/// <param name="box">The area, in degrees.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The charging points, or a failure with a <c>Mobility.*</c> code and a message such as <c>charging points HTTP 500</c>.</returns>
	Task<Result<IReadOnlyList<ChargingPoint>>> GetChargersAsync(GeoBox box, CancellationToken cancellationToken);
	#endregion
}
