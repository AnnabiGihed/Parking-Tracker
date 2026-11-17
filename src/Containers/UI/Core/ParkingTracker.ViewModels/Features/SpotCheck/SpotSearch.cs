using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Where and when the nearest-spot search last ran, so that it is not repeated for every small move.
/// </summary>
/// <param name="Point">The place the search started from.</param>
/// <param name="Time">When the search started.</param>
public sealed record SpotSearch(GeoPoint Point, DateTimeOffset Time);
