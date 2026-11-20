namespace ParkingTracker.Domain.Features.Vehicles.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A plug the vehicle can charge with, to match it against the connectors of a charging point. The device database
///              stores the number of each value, so a value is never renumbered and a new one takes the next free number.
/// </summary>
public enum ChargingConnector
{
	/// <summary>
	/// The European AC plug (IEC 62196 Type 2, "Mennekes").
	/// </summary>
	Type2 = 0,

	/// <summary>
	/// The Combined Charging System DC plug (CCS2 in Europe).
	/// </summary>
	Ccs = 1,

	/// <summary>
	/// The CHAdeMO DC plug.
	/// </summary>
	Chademo = 2,

	/// <summary>
	/// The older AC plug of American and Asian vehicles (SAE J1772 Type 1).
	/// </summary>
	Type1 = 3,
}
