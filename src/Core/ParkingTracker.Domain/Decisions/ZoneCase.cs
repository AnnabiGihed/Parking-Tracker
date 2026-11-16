using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.Zones;

namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The values that travel together through the later steps of a decision: the question, the street surfaces around
///              the place, the surface reading and the zone type derived from it.
/// </summary>
/// <param name="Question">The card, the place and the moment.</param>
/// <param name="Zones">The street surfaces around the place.</param>
/// <param name="Reading">The matched surface with its live rule.</param>
/// <param name="Type">The zone type, or <see langword="null"/> when the rules could not be checked.</param>
internal sealed record ZoneCase(SpotQuestion Question, IReadOnlyList<ZoneSurface> Zones, ZoneReading Reading, ZoneType? Type);
