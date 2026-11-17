using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;

namespace ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The street surface an answer is about, with the live parking rule found for its zone.
/// </summary>
/// <param name="Surface">The street surface the position was matched to.</param>
/// <param name="Rule">The live rule of the zone, or <see langword="null"/> when none was found or it could not be read.</param>
/// <param name="RuleError">Why the rule could not be read, or <see langword="null"/>.</param>
public sealed record ZoneReading(ZoneSurface Surface, ParkingRule? Rule, string? RuleError);
