namespace ParkingTracker.Domain.Features.Vehicles.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The values of a vehicle profile as the screens and the device database exchange them with
///              <see cref="Aggregates.Vehicle"/>. The values are not checked here: the aggregate checks them, with the current year
///              that only the caller knows, so that the domain stays free of the clock.
/// </summary>
/// <param name="Make">The code of a make of <see cref="VehicleMakes"/>.</param>
/// <param name="Model">The model as typed, at most 60 characters after trimming; empty or <see langword="null"/> when unknown.</param>
/// <param name="Year">The model year, from 1950 to the year after the current one.</param>
/// <param name="Fuel">What the vehicle runs on.</param>
/// <param name="EuroNorm">The emission standard, or <see cref="ValueObjects.EuroNorm.Unknown"/>.</param>
/// <param name="Charging">How the vehicle charges; only an electric vehicle or a plug-in hybrid has it, and it may be
/// <see langword="null"/> for them when unknown.</param>
/// <param name="Plate">The number plate as typed, or <see langword="null"/>; kept apart from the plate of the residents' card.</param>
public sealed record VehicleDetails(
	string Make,
	string? Model,
	int Year,
	FuelType Fuel,
	EuroNorm EuroNorm,
	ChargingDetails? Charging = null,
	string? Plate = null);
