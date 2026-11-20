using ParkingTracker.Domain.Features.Vehicles.ValueObjects;

namespace ParkingTracker.ViewModels.Features.Vehicles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The lists of choices of the My vehicle dialog with the readable text of each: the makes of the domain list, the fuels, the
///              emission standards and the plugs. The texts live here and not in the domain, because the domain stores codes and numbers
///              that are never renamed, while the texts are what the user reads.
/// </summary>
public static class VehicleOptions
{
	#region Static Instances
	/// <summary>
	/// Gets the makes in the order of <see cref="VehicleMakes.All"/>, with "Other" last.
	/// </summary>
	public static readonly IReadOnlyList<VehicleOption<string>> Makes = Array.AsReadOnly(
		[.. VehicleMakes.All.Select(make => new VehicleOption<string>(make.Code, make.Name))]);

	/// <summary>
	/// Gets the fuels with their readable names.
	/// </summary>
	public static readonly IReadOnlyList<VehicleOption<FuelType>> Fuels = Array.AsReadOnly(
	[
		new VehicleOption<FuelType>(FuelType.Petrol, "Petrol"),
		new VehicleOption<FuelType>(FuelType.Diesel, "Diesel"),
		new VehicleOption<FuelType>(FuelType.Hybrid, "Hybrid"),
		new VehicleOption<FuelType>(FuelType.PlugInHybrid, "Plug-in hybrid"),
		new VehicleOption<FuelType>(FuelType.Electric, "Electric"),
		new VehicleOption<FuelType>(FuelType.Gas, "LPG / CNG"),
	]);

	/// <summary>
	/// Gets the emission standards, Euro 1 to Euro 6 and then "Unknown".
	/// </summary>
	public static readonly IReadOnlyList<VehicleOption<EuroNorm>> EuroNorms = Array.AsReadOnly(
	[
		new VehicleOption<EuroNorm>(EuroNorm.Euro1, "Euro 1"),
		new VehicleOption<EuroNorm>(EuroNorm.Euro2, "Euro 2"),
		new VehicleOption<EuroNorm>(EuroNorm.Euro3, "Euro 3"),
		new VehicleOption<EuroNorm>(EuroNorm.Euro4, "Euro 4"),
		new VehicleOption<EuroNorm>(EuroNorm.Euro5, "Euro 5"),
		new VehicleOption<EuroNorm>(EuroNorm.Euro6, "Euro 6"),
		new VehicleOption<EuroNorm>(EuroNorm.Unknown, "Unknown"),
	]);

	/// <summary>
	/// Gets the plugs with their readable names.
	/// </summary>
	public static readonly IReadOnlyList<VehicleOption<ChargingConnector>> Connectors = Array.AsReadOnly(
	[
		new VehicleOption<ChargingConnector>(ChargingConnector.Type2, "Type 2"),
		new VehicleOption<ChargingConnector>(ChargingConnector.Ccs, "CCS"),
		new VehicleOption<ChargingConnector>(ChargingConnector.Chademo, "CHAdeMO"),
		new VehicleOption<ChargingConnector>(ChargingConnector.Type1, "Type 1"),
	]);
	#endregion
}
