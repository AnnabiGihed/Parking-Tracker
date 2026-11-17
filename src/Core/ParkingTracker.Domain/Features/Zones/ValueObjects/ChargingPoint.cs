using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Features.Zones.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A charging station for electric vehicles, shown as a marker on the map layer. The values are kept as the service
///              wrote them, because the map only prints them.
/// </summary>
/// <param name="Point">Where the station is.</param>
/// <param name="Operator">Name of the operator, or <see langword="null"/>.</param>
/// <param name="Points">Number of charging points as text, or <see langword="null"/>.</param>
/// <param name="Power">Power as text, or <see langword="null"/>.</param>
/// <param name="Standard">Connector standard, or <see langword="null"/>.</param>
/// <param name="Type">Connector type, or <see langword="null"/>.</param>
public sealed record ChargingPoint(
	GeoPoint Point,
	string? Operator,
	string? Points,
	string? Power,
	string? Standard,
	string? Type);
