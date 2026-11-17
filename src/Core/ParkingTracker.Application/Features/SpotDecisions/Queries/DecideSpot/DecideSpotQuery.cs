using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.SpotDecisions.Queries.DecideSpot;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks "may I park here with my card?" for a GPS fix or a pin. A source that cannot be read gives the answer
///              "can't verify" with the reason; a failed result only means the input was invalid or the shipped data is broken.
/// </summary>
/// <param name="Latitude">The latitude in degrees, -90 to 90.</param>
/// <param name="Longitude">The longitude in degrees, -180 to 180.</param>
/// <param name="AccuracyMeters">The GPS accuracy in metres, or <see langword="null"/> for a pin placed by hand.</param>
public sealed record DecideSpotQuery(double Latitude, double Longitude, double? AccuracyMeters) : IQuery<SpotDecision>;
