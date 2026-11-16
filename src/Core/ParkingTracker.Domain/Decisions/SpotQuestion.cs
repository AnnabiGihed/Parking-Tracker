using ParkingTracker.Domain.Geometry;

namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The question "may I park here with my card?": the card and street lists, the place, how precise it is, and the
///              moment. Everything a decision needs besides the live data it asks for.
/// </summary>
/// <param name="Context">The card and its street lists.</param>
/// <param name="Point">The place.</param>
/// <param name="AccuracyMetres">The GPS accuracy, or <see langword="null"/> for a pin placed by hand.</param>
/// <param name="Now">The current moment on the clock of the device; its local date is the date of the day.</param>
/// <param name="IsOnline">Whether the device has an internet connection.</param>
public sealed record SpotQuestion(DecisionContext Context, GeoPoint Point, double? AccuracyMetres, DateTimeOffset Now, bool IsOnline);
