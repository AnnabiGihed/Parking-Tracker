using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;

namespace ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The status of one street surface for the user's card, used by the map and the free-spot search.
/// </summary>
/// <param name="Surface">The street surface.</param>
/// <param name="Rule">The live parking rule of its zone, or <see langword="null"/>.</param>
/// <param name="Color">The colour name of the zone, for example <c>green</c>; <c>unknown</c> when the code is not known.</param>
/// <param name="Kind">What the surface means for the card.</param>
/// <param name="Street">The street in the card's sector, for City of Brussels cards; otherwise <see langword="null"/>.</param>
/// <param name="Near">The house numbers along the surface with the rule that covers them; empty when not evaluated.</param>
public sealed record SurfaceStatus(
	ZoneSurface Surface,
	ParkingRule? Rule,
	string Color,
	SurfaceKind Kind,
	StreetRules? Street,
	IReadOnlyList<NearAddress> Near);
