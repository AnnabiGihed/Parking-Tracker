using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Zones;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the parking.brussels WFS service: street surfaces with their zone, individual parking spaces and
///              the sector areas (mailles). Nothing is cached between calls and a failure is returned, not thrown, with a
///              message that can be shown to the user, for example <c>parking.brussels HTTP 500</c>.
/// </summary>
public interface IParkingBrusselsClient
{
	#region Methods
	/// <summary>
	/// Gets the street surfaces of the regulation zones in a box. Tunnels, viaducts and disabled surfaces are left out.
	/// </summary>
	/// <param name="box">The area, in degrees.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The surfaces, or a failure with a <c>ParkingBrussels.*</c> code.</returns>
	Task<Result<IReadOnlyList<ZoneSurface>>> GetZonesAsync(GeoBox box, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the individual on-street parking spaces in a box, with their reservation type.
	/// </summary>
	/// <param name="box">The area, in degrees.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The spaces, or a failure with a <c>ParkingBrussels.*</c> code.</returns>
	Task<Result<IReadOnlyList<ReservedSpace>>> GetSpacesAsync(GeoBox box, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the sector areas (mailles) in a box.
	/// </summary>
	/// <param name="box">The area, in degrees.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The areas, or a failure with a <c>ParkingBrussels.*</c> code.</returns>
	Task<Result<IReadOnlyList<SectorMaille>>> GetMaillesAsync(GeoBox box, CancellationToken cancellationToken);
	#endregion
}
