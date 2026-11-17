using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Zones.Services;

namespace ParkingTracker.Domain.Features.Zones.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One on-street parking space of parking.brussels with its reservation type.
/// </summary>
/// <param name="Point">Where the space is.</param>
/// <param name="ReservationType">The <c>typres</c> value; 0 is an ordinary space.</param>
public sealed record ReservedSpace(GeoPoint Point, int ReservationType)
{
	#region Properties
	/// <summary>
	/// Gets the name of the reservation for the user, or <see langword="null"/> when the type is unknown or ordinary.
	/// </summary>
	public string? Label => ReservedSpaces.LabelOf(ReservationType);

	/// <summary>
	/// Gets a value indicating whether the space is reserved for a use (it is not an ordinary space).
	/// </summary>
	public bool IsReserved => ReservationType != 0;

	/// <summary>
	/// Gets a value indicating whether the space is a time-limited zone.
	/// </summary>
	public bool IsTimeLimited => ReservedSpaces.IsTimeLimited(ReservationType);
	#endregion
}
