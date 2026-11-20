namespace ParkingTracker.Domain.Features.Vehicles.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the vehicle runs on, which decides whether it can charge and how a low-emission zone treats it. The device
///              database stores the number of each value, so a value is never renumbered and a new one takes the next free number.
/// </summary>
public enum FuelType
{
	/// <summary>
	/// Petrol only.
	/// </summary>
	Petrol = 0,

	/// <summary>
	/// Diesel only.
	/// </summary>
	Diesel = 1,

	/// <summary>
	/// A hybrid that cannot be plugged in.
	/// </summary>
	Hybrid = 2,

	/// <summary>
	/// A hybrid that charges from a plug.
	/// </summary>
	PlugInHybrid = 3,

	/// <summary>
	/// A battery electric vehicle.
	/// </summary>
	Electric = 4,

	/// <summary>
	/// Natural gas or LPG.
	/// </summary>
	Gas = 5,
}
