using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.Vehicles.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of the vehicle profile; each code is stable and dotted, each message tells the user what to fix.
/// </summary>
public static class VehicleErrors
{
	#region Static Instances
	/// <summary>
	/// The make is not a code of the curated list.
	/// </summary>
	public static readonly Error MakeUnknown = new("Vehicle.Make.Unknown", "Pick the make from the list, or Other.");

	/// <summary>
	/// The model is longer than 60 characters.
	/// </summary>
	public static readonly Error ModelTooLong = new("Vehicle.Model.TooLong", "The model can be at most 60 characters long.");

	/// <summary>
	/// The fuel is not a known value.
	/// </summary>
	public static readonly Error FuelUnknown = new("Vehicle.Fuel.Unknown", "Pick what the vehicle runs on from the list.");

	/// <summary>
	/// The emission standard is not a known value.
	/// </summary>
	public static readonly Error EuroNormUnknown = new("Vehicle.EuroNorm.Unknown", "Pick the emission standard from the list.");

	/// <summary>
	/// Charging details are set for a vehicle that does not plug in.
	/// </summary>
	public static readonly Error ChargingNotAllowed = new("Vehicle.Charging.NotAllowed", "Only an electric vehicle or a plug-in hybrid has plugs, a battery and a range.");

	/// <summary>
	/// A plug is not a known value.
	/// </summary>
	public static readonly Error ConnectorUnknown = new("Vehicle.Charging.ConnectorUnknown", "Pick the plugs from the list.");

	/// <summary>
	/// The battery is smaller than 1 kWh or larger than 250 kWh.
	/// </summary>
	public static readonly Error BatteryOutOfRange = new("Vehicle.Charging.BatteryOutOfRange", "The battery holds between 1 and 250 kWh.");

	/// <summary>
	/// The range is shorter than 10 km or longer than 1000 km.
	/// </summary>
	public static readonly Error RangeOutOfRange = new("Vehicle.Charging.RangeOutOfRange", "The range is between 10 and 1000 km.");
	#endregion

	#region Factory Methods
	/// <summary>
	/// Builds the error for a model year before 1950 or after the year following the current one.
	/// </summary>
	/// <param name="latestYear">The latest year allowed (the current year plus one), or <see langword="null"/> when only the earliest
	/// year was checked.</param>
	/// <returns>The error.</returns>
	public static Error YearOutOfRange(int? latestYear)
	{
		return latestYear is { } latest
			? new Error("Vehicle.Year.OutOfRange", $"The model year is between 1950 and {latest}.")
			: new Error("Vehicle.Year.OutOfRange", "The model year is 1950 or later.");
	}
	#endregion
}
