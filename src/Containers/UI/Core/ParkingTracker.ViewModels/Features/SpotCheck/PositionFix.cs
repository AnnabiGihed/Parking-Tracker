using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The last fix of the user together with the moment it arrived on the phone, which tells how long ago the signal was lost.
/// </summary>
/// <param name="Fix">The fix.</param>
/// <param name="ReceivedAt">When the app received it.</param>
public sealed record PositionFix(GpsFix Fix, DateTimeOffset ReceivedAt);
