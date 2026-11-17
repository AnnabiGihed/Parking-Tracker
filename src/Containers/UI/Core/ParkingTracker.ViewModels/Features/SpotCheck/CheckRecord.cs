using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the last spot check was about, so that the tracking rules can tell whether a new fix is worth another check.
/// </summary>
/// <param name="Point">The place that was checked.</param>
/// <param name="Time">When the check started.</param>
/// <param name="Source">Whether it was the position of the user or a pin.</param>
/// <param name="AccuracyMetres">The accuracy of the position in metres, or <see langword="null"/> for a pin.</param>
public sealed record CheckRecord(GeoPoint Point, DateTimeOffset Time, CheckSource Source, double? AccuracyMetres);
