using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;

namespace ParkingTracker.Domain.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Evaluates all street surfaces of an area for the user's card (the map layer) and finds the nearest spots where
///              the card is valid. It works on data that is already fetched; the caller asks which sources are needed.
/// </summary>
public static class AreaDecider
{
	#region Constants
	/// <summary>
	/// Spots closer than this, in metres, are where the user already is and are not suggested.
	/// </summary>
	public const double MinimumSpotDistanceMetres = 3;

	/// <summary>
	/// How far from the user, in degrees of latitude, the free-spot search looks.
	/// </summary>
	public const double SearchDelta = 0.0035;
	#endregion

	#region Public Methods
	/// <summary>
	/// Tells whether the sector areas (mailles) are needed to evaluate surfaces for this card.
	/// </summary>
	/// <param name="context">The card and its municipality.</param>
	/// <returns><see langword="true"/> for area-based municipalities and for cards with border zones.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
	public static bool NeedsMailles(DecisionContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		return context.Municipality.Kind == MunicipalityKind.Mailles || context.Lims.Count > 0;
	}

	/// <summary>
	/// Tells whether the official address points are needed to evaluate surfaces for this card.
	/// </summary>
	/// <param name="context">The card and its municipality.</param>
	/// <returns><see langword="true"/> for City of Brussels cards with a loaded sector.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
	public static bool NeedsAddresses(DecisionContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		return context.Municipality.Kind == MunicipalityKind.Streets && context.Sector is not null;
	}

	/// <summary>
	/// Tells whether a free-spot search makes sense: the card is valid and the street lists are confirmed.
	/// </summary>
	/// <param name="context">The card and its street lists.</param>
	/// <param name="today">The local calendar day.</param>
	/// <returns><see langword="true"/> when the search can give an answer.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
	public static bool CanSearchSpots(DecisionContext context, DateOnly today)
	{
		ArgumentNullException.ThrowIfNull(context);
		return CardState.Evaluate(context.Card?.Period, today).IsValid && context.ListProblem is null;
	}

	/// <summary>
	/// Evaluates the surfaces of an area. A surface is part of the area when one of its outer vertices is inside the box.
	/// </summary>
	/// <param name="context">The card and its street lists.</param>
	/// <param name="box">Bounding box of the searched area.</param>
	/// <param name="zones">The surfaces fetched around the area (with padding).</param>
	/// <param name="rules">The live rules by zone code, or <see langword="null"/> when they could not be fetched.</param>
	/// <param name="mailles">The sector areas, empty when not needed.</param>
	/// <param name="addressPoints">The official address points, empty when not needed.</param>
	/// <returns>The status of each surface in the area.</returns>
	/// <exception cref="ArgumentNullException">An argument other than <paramref name="rules"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<SurfaceStatus> Evaluate(
		DecisionContext context,
		GeoBox box,
		IReadOnlyList<ZoneSurface> zones,
		IReadOnlyDictionary<string, ParkingRule>? rules,
		IReadOnlyList<SectorMaille> mailles,
		IReadOnlyList<AddressPoint> addressPoints)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(zones);
		ArgumentNullException.ThrowIfNull(mailles);
		ArgumentNullException.ThrowIfNull(addressPoints);
		var addresses = NeedsAddresses(context) ? AddressAssigner.Assign(addressPoints, zones, context.Sector) : [];
		return zones
			.Where(zone => IsInBox(zone, box))
			.Select(zone => SurfaceStatusEvaluator.Evaluate(context, zone, addresses, rules, mailles))
			.ToList();
	}

	/// <summary>
	/// Finds the nearest covered spots, one per street, nearest first.
	/// </summary>
	/// <param name="origin">Where the search starts.</param>
	/// <param name="surfaces">The evaluated surfaces around the origin.</param>
	/// <param name="count">The maximum number of spots.</param>
	/// <returns>Up to <paramref name="count"/> spots.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="surfaces"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<FreeSpot> FindFreeSpots(GeoPoint origin, IReadOnlyList<SurfaceStatus> surfaces, int count = 3)
	{
		ArgumentNullException.ThrowIfNull(surfaces);
		return surfaces
			.SelectMany(status => SpotsOf(origin, status))
			.Where(spot => spot.Metres >= MinimumSpotDistanceMetres)
			.OrderBy(spot => spot.Metres)
			.GroupBy(spot => NameNormalizer.Normalize(spot.Street), StringComparer.Ordinal)
			.Select(group => group.First())
			.Take(count)
			.ToList();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Determines whether a zone surface touches the box.
	/// </summary>
	/// <param name="zone">Zone surface to test.</param>
	/// <param name="box">Bounding box of the searched area.</param>
	/// <returns><see langword="true"/> when a zone surface touches the box; otherwise <see langword="false"/>.</returns>
	private static bool IsInBox(ZoneSurface zone, GeoBox box)
	{
		return zone.Shape.Polygons.Any(polygon => polygon.Count > 0 && polygon[0].Any(vertex =>
		{
			var position = WebMercator.ToGeo(vertex);
			return position.Latitude >= box.South && position.Latitude <= box.North && position.Longitude >= box.West && position.Longitude <= box.East;
		}));
	}

	/// <summary>
	/// Lists the free spots of a surface status, with their distance from the origin.
	/// </summary>
	/// <param name="origin">Position the free spots are measured from.</param>
	/// <param name="status">Status of the surface whose spots are listed.</param>
	/// <returns>The free spots.</returns>
	private static IEnumerable<FreeSpot> SpotsOf(GeoPoint origin, SurfaceStatus status)
	{
		var surface = status.Surface;
		if (status.Kind == SurfaceKind.Covered)
		{
			var closest = surface.Shape.ClosestTo(origin);
			yield return new FreeSpot(closest.Point, GeoMath.Distance(origin, closest.Point), surface.FrenchName, surface.Municipality, status.Street?.FrenchName ?? surface.FrenchName);
		}
		else if (status.Kind == SurfaceKind.Mixed)
		{
			foreach (var address in status.Near.Where(entry => entry.IsCovered).Select(entry => entry.Address))
			{
				var closest = surface.Shape.ClosestTo(address.Position);
				yield return new FreeSpot(closest.Point, GeoMath.Distance(origin, closest.Point), surface.FrenchName, surface.Municipality, $"{status.Street!.FrenchName} {address.Text}");
			}
		}
	}
	#endregion
}
