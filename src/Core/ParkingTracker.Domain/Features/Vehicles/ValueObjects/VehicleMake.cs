namespace ParkingTracker.Domain.Features.Vehicles.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One make of the curated list <see cref="VehicleMakes"/>: the stable code that the device database stores and the
///              brand name the screens show. Brand names are the same in every language, so the name is not translated.
/// </summary>
/// <param name="Code">The stable code, in lower case with dashes, for example <c>mercedes-benz</c>; never renamed once released.</param>
/// <param name="Name">The brand name, for example <c>Mercedes-Benz</c>.</param>
public sealed record VehicleMake(string Code, string Name);
