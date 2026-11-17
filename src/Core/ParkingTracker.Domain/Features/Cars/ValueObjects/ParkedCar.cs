using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Features.Cars.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Where and when the user parked the car, with what the app said at that moment, so that the user can walk back to
///              it. It is saved as the <c>pt.car</c> value of the first version of the app.
/// </summary>
/// <param name="Point">Where the car is.</param>
/// <param name="Time">When the position was saved.</param>
/// <param name="Status">The headline the app showed, for example <c>Paid parking</c>; empty when unknown.</param>
/// <param name="Where">Where the car is, in words; empty when unknown.</param>
public sealed record ParkedCar(GeoPoint Point, DateTimeOffset Time, string Status, string Where);
