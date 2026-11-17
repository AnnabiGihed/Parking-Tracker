using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One position report of the phone's location service.
/// </summary>
/// <param name="Point">The reported position.</param>
/// <param name="AccuracyMetres">The accuracy radius in metres.</param>
/// <param name="SpeedMetresPerSecond">The speed reported by the device, or <see langword="null"/> when it reports none.</param>
/// <param name="Time">When the fix was taken.</param>
public sealed record GpsFix(GeoPoint Point, double AccuracyMetres, double? SpeedMetresPerSecond, DateTimeOffset Time);
