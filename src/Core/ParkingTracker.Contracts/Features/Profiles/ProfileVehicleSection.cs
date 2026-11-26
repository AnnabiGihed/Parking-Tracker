namespace ParkingTracker.Contracts.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The vehicle profile of the profile document. The fuel, the emission norm and the connectors are the names of the
///              domain enumerations <c>FuelType</c>, <c>EuroNorm</c> and <c>ChargingConnector</c>, so the document does not depend on
///              their numbers.
/// </summary>
/// <param name="Make">The code of the make.</param>
/// <param name="Model">The model, or <see langword="null"/> when unknown.</param>
/// <param name="Year">The model year.</param>
/// <param name="Fuel">The name of the fuel type, for example <c>Electric</c>.</param>
/// <param name="EuroNorm">The name of the emission norm, for example <c>Euro6</c>.</param>
/// <param name="Connectors">The names of the charging connectors; empty for a vehicle that does not plug in.</param>
/// <param name="BatteryKwh">The battery in kWh, or <see langword="null"/>.</param>
/// <param name="RangeKm">The range on a full battery in kilometres, or <see langword="null"/>.</param>
/// <param name="Plate">The number plate, or <see langword="null"/>.</param>
/// <param name="UpdatedAt">The time the phone changed the vehicle.</param>
public sealed record ProfileVehicleSection(
	string Make,
	string? Model,
	int Year,
	string Fuel,
	string EuroNorm,
	IReadOnlyList<string> Connectors,
	double? BatteryKwh,
	int? RangeKm,
	string? Plate,
	DateTimeOffset UpdatedAt);
