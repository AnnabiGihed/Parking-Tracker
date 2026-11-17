namespace ParkingTracker.Domain.Features.Zones.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Whether a disturbance of parking is a roadworks site or a traffic event.
/// </summary>
public enum WorkKind
{
	/// <summary>
	/// A roadworks site (Osiris).
	/// </summary>
	Works,

	/// <summary>
	/// A traffic event (Brussels Mobility).
	/// </summary>
	Event,
}
