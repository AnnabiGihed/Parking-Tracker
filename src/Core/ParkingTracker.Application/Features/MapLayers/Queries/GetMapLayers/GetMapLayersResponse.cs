using ParkingTracker.Domain.Features.Zones.ValueObjects;

namespace ParkingTracker.Application.Features.MapLayers.Queries.GetMapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The data of the map layers of one area. A layer that could not be read is empty and named in the failures, so
///              that the other layers still show, as the first version of the map did.
/// </summary>
/// <param name="MyAreas">The outlines of the areas and border zones on the user's card.</param>
/// <param name="Spaces">The individual parking spaces.</param>
/// <param name="Works">The roadworks and traffic events running now.</param>
/// <param name="CarParks">The public car parks.</param>
/// <param name="Chargers">The charging points.</param>
/// <param name="Failures">Why a layer could not be read, by layer name (<see cref="AreasLayer"/>, <see cref="SpacesLayer"/>, <see cref="WorksLayer"/>, <see cref="CarParksLayer"/>, <see cref="ChargersLayer"/>).</param>
public sealed record GetMapLayersResponse(
	IReadOnlyList<SectorMaille> MyAreas,
	IReadOnlyList<ReservedSpace> Spaces,
	IReadOnlyList<WorkItem> Works,
	IReadOnlyList<CarPark> CarParks,
	IReadOnlyList<ChargingPoint> Chargers,
	IReadOnlyDictionary<string, string> Failures)
{
	#region Constants
	/// <summary>
	/// The name of the areas outline layer in <see cref="Failures"/>.
	/// </summary>
	public const string AreasLayer = "areas";

	/// <summary>
	/// The name of the parking spaces layer in <see cref="Failures"/>.
	/// </summary>
	public const string SpacesLayer = "spaces";

	/// <summary>
	/// The name of the roadworks and events layer in <see cref="Failures"/>.
	/// </summary>
	public const string WorksLayer = "works";

	/// <summary>
	/// The name of the car parks layer in <see cref="Failures"/>.
	/// </summary>
	public const string CarParksLayer = "carparks";

	/// <summary>
	/// The name of the charging points layer in <see cref="Failures"/>.
	/// </summary>
	public const string ChargersLayer = "chargers";
	#endregion
}
