using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.Services;
using ParkingTracker.Domain.Features.Zones.ValueObjects;

namespace ParkingTracker.Domain.Features.Cards.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Decides whether the user's card covers a place in a zone that accepts residents' cards, by the kind of the
///              card's municipality: street lists, areas (mailles), the whole municipality, blue zones only, or unknown.
/// </summary>
public static class CardScopeEvaluator
{
	#region Constants
	/// <summary>
	/// How close to an area of the card, in metres, a place still counts as at its limit.
	/// </summary>
	public const double BorderMarginMetres = 12;

	/// <summary>
	/// The colour code of the blue zones.
	/// </summary>
	private const int BlueColour = 5;
	#endregion

	#region Public Methods
	/// <summary>
	/// Decides whether the card covers a place.
	/// </summary>
	/// <param name="context">The card and its municipality.</param>
	/// <param name="zone">The street surface at the place.</param>
	/// <param name="point">Position the question is about.</param>
	/// <param name="mailles">The sector areas published around the place; empty when none were fetched.</param>
	/// <param name="accuracyMetres">The GPS accuracy, which widens the margin at an area's limit; 0 when unknown.</param>
	/// <returns>The scope, with the reason when it is not the street lists.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static CardScope Evaluate(DecisionContext context, ZoneSurface zone, GeoPoint point, IReadOnlyList<SectorMaille> mailles, double accuracyMetres = 0)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(zone);
		ArgumentNullException.ThrowIfNull(mailles);
		var municipality = context.Municipality;
		var border = mailles.FirstOrDefault(m => context.Lims.Contains(m.ZoneCode) && m.Shape.ClosestTo(point).Metres == 0);
		if (border is not null)
		{
			return CardScope.Yes($"border zone {border.DisplayName} (on your card)");
		}

		if (!string.Equals(NameNormalizer.Normalize(zone.Municipality), NameNormalizer.Normalize(municipality.Mu), StringComparison.Ordinal))
		{
			return municipality.Kind == MunicipalityKind.Streets
				? CardScope.Streets()
				: CardScope.No($"is in {NameNormalizer.TitleCase(zone.Municipality)}, not in {municipality.Name}");
		}

		return municipality.Kind switch
		{
			MunicipalityKind.Streets => CardScope.Streets(),
			MunicipalityKind.Whole => CardScope.Yes($"your card is valid in this zone everywhere in {municipality.Name}"),
			MunicipalityKind.Wsl => IsBlue(zone)
				? CardScope.Yes($"blue zone: your card is valid in all blue zones of {municipality.Name}")
				: CardScope.Check(municipality.Note ?? string.Empty),
			MunicipalityKind.Unknown => CardScope.Check(municipality.Note ?? string.Empty),
			MunicipalityKind.Mailles => EvaluateMailles(context, point, mailles, accuracyMetres),
			_ => CardScope.Check("unknown municipality rules"),
		};
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Determines whether the zone is a blue (residents) zone.
	/// </summary>
	/// <param name="zone">Zone surface to test.</param>
	/// <returns><see langword="true"/> when the zone is a blue (residents) zone; otherwise <see langword="false"/>.</returns>
	private static bool IsBlue(ZoneSurface zone)
	{
		return ZoneClassifier.Classify(zone.ColourCode, null) == ZoneType.Disk || zone.ColourCode == BlueColour;
	}

	/// <summary>
	/// Evaluates the card scope against the sector mailles around the point.
	/// </summary>
	/// <param name="context">The decision context.</param>
	/// <param name="point">Position the question is about.</param>
	/// <param name="mailles">Sector mailles (grid cells) that cover the searched area.</param>
	/// <param name="accuracyMetres">GPS accuracy radius in metres.</param>
	/// <returns>The scope of the card at the point.</returns>
	private static CardScope EvaluateMailles(DecisionContext context, GeoPoint point, IReadOnlyList<SectorMaille> mailles, double accuracyMetres)
	{
		if (context.Mailles.Count == 0)
		{
			return CardScope.Check("choose the areas (mailles) printed on your card in the card settings");
		}

		var mine = mailles.Where(m => context.Mailles.Contains(m.ZoneCode)).ToList();
		var hit = mine.FirstOrDefault(m => m.Shape.ClosestTo(point).Metres == 0);
		if (hit is not null)
		{
			return CardScope.Yes($"in your area {hit.DisplayName}");
		}

		var nearest = mine.Select(m => (Maille: m, Metres: m.Shape.ClosestTo(point).Metres)).OrderBy(entry => entry.Metres).FirstOrDefault();
		return nearest.Maille is not null && nearest.Metres <= Math.Max(BorderMarginMetres, accuracyMetres)
			? CardScope.Check($"you are at the limit of your area {nearest.Maille.DisplayName}: check the street signs")
			: CardScope.No("is outside the areas on your card");
	}
	#endregion
}
