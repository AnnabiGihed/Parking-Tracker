using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Naming;
using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.Zones;

namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Works out the status of one street surface for the user's card, from the zone rule, the card's scope and, for
///              the City of Brussels, the street list and the house numbers along the surface.
/// </summary>
public static class SurfaceStatusEvaluator
{
	#region Constants
	private const int RedColour = 1;
	#endregion

	#region Public Methods
	/// <summary>
	/// Evaluates a surface.
	/// </summary>
	/// <param name="context">The card and its street lists.</param>
	/// <param name="surface">The surface.</param>
	/// <param name="addresses">The assigned addresses around the surface; only used for City of Brussels cards.</param>
	/// <param name="rules">The live rules by zone code, or <see langword="null"/> when they could not be fetched.</param>
	/// <param name="mailles">The sector areas around the surface.</param>
	/// <returns>The status.</returns>
	/// <exception cref="ArgumentNullException">An argument other than <paramref name="rules"/> is <see langword="null"/>.</exception>
	public static SurfaceStatus Evaluate(
		DecisionContext context,
		ZoneSurface surface,
		IReadOnlyList<AssignedAddress> addresses,
		IReadOnlyDictionary<string, ParkingRule>? rules,
		IReadOnlyList<SectorMaille> mailles)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(addresses);
		ArgumentNullException.ThrowIfNull(mailles);
		ParkingRule? rule = null;
		if (surface.ZoneCode is not null)
		{
			rules?.TryGetValue(surface.ZoneCode, out rule);
		}

		var color = ZoneNames.Of(surface.ColourCode) ?? "unknown";
		SurfaceStatus Make(SurfaceKind kind) => new(surface, rule, color, kind, null, []);
		var type = ZoneClassifier.Classify(surface.ColourCode, rule);
		switch (type)
		{
			case ZoneType.Free:
				return Make(SurfaceKind.Free);
			case ZoneType.Payable:
				return Make(surface.ColourCode == RedColour ? SurfaceKind.Red : SurfaceKind.Pay);
			case ZoneType.Reserved:
				return Make(SurfaceKind.Pay);
			case null:
				return Make(SurfaceKind.Unknown);
			default:
				break;
		}

		if (ZoneClassifier.IsEvent(surface.ColourCode, rule))
		{
			return Make(SurfaceKind.Unknown);
		}

		var notCovered = type == ZoneType.Disk ? SurfaceKind.Disc : SurfaceKind.Pay;
		var scope = CardScopeEvaluator.Evaluate(context, surface, surface.BoundsCenter, mailles);
		return scope.State switch
		{
			CardScopeState.Yes => Make(SurfaceKind.Covered),
			CardScopeState.No => Make(notCovered),
			CardScopeState.Check => Make(SurfaceKind.Check),
			_ => EvaluateStreetList(context, surface, addresses, rule, color, notCovered),
		};
	}
	#endregion

	#region Private Helpers
	private static SurfaceStatus EvaluateStreetList(DecisionContext context, ZoneSurface surface, IReadOnlyList<AssignedAddress> addresses, ParkingRule? rule, string color, SurfaceKind notCovered)
	{
		SurfaceStatus Make(SurfaceKind kind, StreetRules? street = null, IReadOnlyList<NearAddress>? near = null) => new(surface, rule, color, kind, street, near ?? []);
		if (context.Sector is null)
		{
			return Make(SurfaceKind.Check);
		}

		var street = context.Sector.FindStreet(surface.FrenchName, surface.DutchName);
		IReadOnlyList<RuleSet> sets = street is null ? [] : street.RuleSetsFor(surface.Municipality, context.Tolerances);
		if (sets.Count == 0)
		{
			return Make(notCovered, street);
		}

		if (context.ListProblem is not null)
		{
			return Make(SurfaceKind.Unknown, street);
		}

		if (sets.Any(set => set.Odd.IsAll && set.Even.IsAll))
		{
			return Make(SurfaceKind.Covered, street);
		}

		if (sets.Any(set => set.Odd.IsCondition || set.Even.IsCondition))
		{
			return Make(SurfaceKind.Check, street);
		}

		var near = NearAddresses(surface, addresses, street!, sets);
		if (near.Count == 0)
		{
			return Make(SurfaceKind.Check, street, near);
		}

		var covered = near.Count(entry => entry.IsCovered);
		if (covered == near.Count)
		{
			return Make(SurfaceKind.Covered, street, near);
		}

		return Make(covered == 0 ? notCovered : SurfaceKind.Mixed, street, near);
	}

	private static List<NearAddress> NearAddresses(ZoneSurface surface, IReadOnlyList<AssignedAddress> addresses, StreetRules street, IReadOnlyList<RuleSet> sets)
	{
		var near = addresses.Where(address => address.SurfaceId == surface.Id).ToList();
		if (near.Count == 0)
		{
			var commune = NameNormalizer.Normalize(surface.Municipality);
			near = addresses
				.Where(address => address.StreetIndex == street.Index && address.Commune == commune
					&& surface.Shape.ClosestTo(address.Position).Metres <= AddressAssigner.AddressToSurfaceMetres)
				.ToList();
		}

		return near.Select(address => new NearAddress(address, RuleSet.FindCovering(sets, address.House))).ToList();
	}
	#endregion
}
