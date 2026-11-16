using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Zones;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the Brussels Mobility layers that disturb parking: roadworks (Osiris) and traffic events. Car parks and
///              charging points are added with the map layers.
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
	#endregion
}
