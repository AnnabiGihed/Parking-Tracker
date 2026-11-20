namespace ParkingTracker.Domain.Features.Vehicles.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : How a vehicle that plugs in (electric or plug-in hybrid) charges: the plugs it takes, the usable battery and the
///              range on a full battery, as the user typed them. The values are not checked here: <see cref="Aggregates.Vehicle"/>
///              checks them against its rules, so that an invalid entry gives a failed result instead of an exception.
/// </summary>
/// <param name="Connectors">The plugs the vehicle takes; a plug listed twice counts once.</param>
/// <param name="BatteryKwh">The usable battery in kWh, or <see langword="null"/> when unknown.</param>
/// <param name="RangeKm">The range on a full battery in kilometres, or <see langword="null"/> when unknown.</param>
public sealed record ChargingDetails(IReadOnlyCollection<ChargingConnector> Connectors, double? BatteryKwh = null, int? RangeKm = null);
