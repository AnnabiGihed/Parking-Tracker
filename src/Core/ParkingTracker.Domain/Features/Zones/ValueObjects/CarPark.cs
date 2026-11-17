using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Features.Zones.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A public car park of Brussels Mobility, shown as a marker on the map layer. The numbers are kept as the service
///              wrote them, because the map only prints them.
/// </summary>
/// <param name="Point">Where the entrance is.</param>
/// <param name="Name">Name of the car park, or <see langword="null"/>.</param>
/// <param name="Address">Street and house number, or empty.</param>
/// <param name="Capacity">Number of places as text, or <see langword="null"/> when unknown.</param>
/// <param name="DisabledCapacity">Number of places for people with reduced mobility as text, or <see langword="null"/>.</param>
/// <param name="Operator">Name of the operator, or <see langword="null"/>.</param>
/// <param name="Phone">Contact phone number, or <see langword="null"/>.</param>
/// <param name="MaxHeight">Maximum vehicle height in metres as text, or <see langword="null"/> when unknown or zero.</param>
public sealed record CarPark(
	GeoPoint Point,
	string? Name,
	string Address,
	string? Capacity,
	string? DisabledCapacity,
	string? Operator,
	string? Phone,
	string? MaxHeight);
