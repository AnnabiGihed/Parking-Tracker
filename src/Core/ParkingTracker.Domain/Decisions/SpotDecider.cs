using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Cards;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Naming;
using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.ReferenceData;
using ParkingTracker.Domain.Zones;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Decides whether the user's card covers a spot: the card, the zone rule, the card's scope and, for the City of
///              Brussels, the street lists and house numbers. The decision is a chain of <see cref="DecisionStep"/> values:
///              each asks for the live data it needs and continues with the outcome, so no input or output happens here.
///              A source that fails gives the answer "can't verify" with the reason, never a guess. Reserved spaces, roadworks and
///              paid hours are added by <see cref="SpotAnnotator"/> once the zone decision is made.
/// </summary>
public static class SpotDecider
{
	#region Constants
	/// <summary>
	/// The worst GPS accuracy, in metres, for which an answer is given.
	/// </summary>
	public const double MaxGpsAccuracyMetres = 25;

	/// <summary>
	/// How far from a street surface, in metres, a place is still taken to be on it (before adding the GPS accuracy).
	/// </summary>
	public const double ZoneSnapMetres = 8;

	/// <summary>
	/// The most the GPS accuracy adds to <see cref="ZoneSnapMetres"/>, in metres.
	/// </summary>
	public const double ZoneSnapAccuracyCapMetres = 15;

	/// <summary>
	/// How far from the place, in degrees of latitude, the street surfaces are fetched (before padding).
	/// </summary>
	public const double ZoneBoxDelta = 0.0007;

	private const string PayHint = "Pay at the meter or with an app.";
	#endregion

	#region Public Methods
	/// <summary>
	/// Starts the decision.
	/// </summary>
	/// <param name="question">The card, the place and the moment.</param>
	/// <returns>The first step: an answer for a card, connection or GPS problem, otherwise the request for the street surfaces.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="question"/> is <see langword="null"/>.</exception>
	public static DecisionStep Begin(SpotQuestion question)
	{
		ArgumentNullException.ThrowIfNull(question);
		var early = CheckPreconditions(question);
		if (early is not null)
		{
			return new DecisionStep.Decided(early);
		}

		var box = GeoBox.Around(question.Point, ZoneBoxDelta);
		return new DecisionStep.NeedZones(box.Pad(), zones => OnZones(question, box, zones));
	}
	#endregion

	#region Private Helpers
	private static SpotDecision? CheckPreconditions(SpotQuestion question)
	{
		var context = question.Context;
		if (context.Card is null)
		{
			return SpotDecision.CantVerify("Add your residents' parking card first (card button, top right).");
		}

		var cardState = CardState.Evaluate(context.Card.Period, DateOnly.FromDateTime(question.Now.DateTime));
		if (!cardState.IsValid)
		{
			return SpotDecision.Create(Verdict.Pay, cardState.Reason ?? string.Empty);
		}

		if (!question.IsOnline)
		{
			return SpotDecision.CantVerify("No internet connection. The app only answers with live data.");
		}

		return question.AccuracyMetres is { } accuracy && accuracy > MaxGpsAccuracyMetres
			? SpotDecision.CantVerify($"GPS is not precise enough yet (±{JsText.Round(accuracy)} m, need ±{MaxGpsAccuracyMetres} m). Waiting for a better fix…")
			: null;
	}

	private static DecisionStep OnZones(SpotQuestion question, GeoBox box, Result<IReadOnlyList<ZoneSurface>> zones)
	{
		if (zones.IsFailure)
		{
			return Annotate(question, SpotDecision.CantVerify($"Could not reach parking.brussels ({zones.Error.Message})."));
		}

		var accuracy = question.AccuracyMetres ?? 0;
		var (nearest, metres) = NearestSurface(zones.Value, question.Point);
		if (nearest is null || metres > ZoneSnapMetres + Math.Min(accuracy, ZoneSnapAccuracyCapMetres))
		{
			return Annotate(question, SpotDecision.CantVerify("This point is not on a street with parking rules. Put the pin on the street, where you park."));
		}

		var code = nearest.ZoneCode;
		return !string.IsNullOrEmpty(code) && nearest.ColourCode != 0
			? new DecisionStep.NeedRule(code, rules => OnRule(question, box, zones.Value, nearest, code, rules))
			: DecideZone(question, box, zones.Value, new ZoneReading(nearest, null, null));
	}

	private static (ZoneSurface? Surface, double Metres) NearestSurface(IReadOnlyList<ZoneSurface> zones, GeoPoint point)
	{
		ZoneSurface? nearest = null;
		var nearestMetres = double.PositiveInfinity;
		foreach (var zone in zones)
		{
			var distance = zone.Shape.ClosestTo(point).Metres;
			if (distance < nearestMetres)
			{
				nearestMetres = distance;
				nearest = zone;
			}
		}

		return (nearest, nearestMetres);
	}

	private static DecisionStep OnRule(SpotQuestion question, GeoBox box, IReadOnlyList<ZoneSurface> zones, ZoneSurface surface, string code, Result<IReadOnlyDictionary<string, ParkingRule>> rules)
	{
		var reading = rules.IsFailure
			? new ZoneReading(surface, null, rules.Error.Message)
			: new ZoneReading(surface, rules.Value.GetValueOrDefault(code), null);
		return DecideZone(question, box, zones, reading);
	}

	private static DecisionStep DecideZone(SpotQuestion question, GeoBox box, IReadOnlyList<ZoneSurface> zones, ZoneReading reading)
	{
		var surface = reading.Surface;
		var type = ZoneClassifier.Classify(surface.ColourCode, reading.Rule);
		var early = DecideByZoneType(surface, reading, type);
		if (early is not null)
		{
			return Annotate(question, early);
		}

		var context = question.Context;
		if (!AreaDecider.NeedsMailles(context))
		{
			return DecideScope(new ZoneCase(question, zones, reading, type), box, []);
		}

		return new DecisionStep.NeedMailles(box.Pad(), mailles => mailles.IsFailure
			? Annotate(question, SpotDecision.CantVerify($"Could not get the sector map from parking.brussels ({mailles.Error.Message}).", reading))
			: DecideScope(new ZoneCase(question, zones, reading, type), box, mailles.Value));
	}

	private static SpotDecision? DecideByZoneType(ZoneSurface surface, ZoneReading reading, ZoneType? type)
	{
		var where = surface.FrenchName;
		var colourName = ZoneNames.Of(surface.ColourCode) ?? string.Empty;
		if (type == ZoneType.Free)
		{
			return SpotDecision.Create(Verdict.Free, $"{where} has no parking rules: free for everyone.", reading);
		}

		if (ZoneClassifier.IsEvent(surface.ColourCode, reading.Rule))
		{
			return SpotDecision.CantVerify($"{where} is an event zone: the normal rules don't apply. Follow the signs.", reading);
		}

		return type switch
		{
			ZoneType.Payable => SpotDecision.Create(
				Verdict.Pay,
				$"{where} is {ZoneLabel(surface.ColourCode, colourName)}: residents' cards are not valid here. {PayHint}",
				reading),
			ZoneType.Reserved => SpotDecision.Create(
				Verdict.Pay,
				$"{where}: reserved zone ({ReservedName(reading.Rule)}). Residents' cards are not valid here.",
				reading),
			null => SpotDecision.CantVerify($"{where}: the rules of this {colourName} zone could not be checked. Orange zones accept residents' cards only in some municipalities: follow the signs.", reading),
			_ => null,
		};
	}

	private static SpotDecision NotCovered(ZoneType? type, ZoneReading reading, string text)
	{
		return type == ZoneType.Disk
			? SpotDecision.Create(Verdict.ParkingDisc, $"{text} Blue zone: put your parking disc, time is limited.", reading)
			: SpotDecision.Create(Verdict.Pay, $"{text} {PayHint}", reading);
	}

	private static string ZoneLabel(int? colourCode, string colourName) => colourCode == 1 ? "a red zone" : $"a {colourName} zone";

	private static string ReservedName(ParkingRule? rule) => string.IsNullOrEmpty(rule?.Name) ? "reserved use" : rule.Name;

	private static DecisionStep DecideScope(ZoneCase zoneCase, GeoBox box, IReadOnlyList<SectorMaille> mailles)
	{
		var question = zoneCase.Question;
		var surface = zoneCase.Reading.Surface;
		var where = surface.FrenchName;
		var scope = CardScopeEvaluator.Evaluate(question.Context, surface, question.Point, mailles, question.AccuracyMetres ?? 0);
		return scope.State switch
		{
			CardScopeState.Yes => Annotate(question, SpotDecision.Create(Verdict.Free, $"{where}: {scope.Why}.", zoneCase.Reading)),
			CardScopeState.No => Annotate(question, NotCovered(zoneCase.Type, zoneCase.Reading, $"{where} {scope.Why}: your card doesn't cover it.")),
			CardScopeState.Check => Annotate(question, SpotDecision.Create(Verdict.CheckCard, $"{where}: {scope.Why}.", zoneCase.Reading)),
			_ => DecideStreetList(zoneCase, box),
		};
	}

	private static DecisionStep DecideStreetList(ZoneCase zoneCase, GeoBox box)
	{
		var question = zoneCase.Question;
		var reading = zoneCase.Reading;
		var context = question.Context;
		var surface = reading.Surface;
		var where = surface.FrenchName;
		if (context.Sector is null)
		{
			return Annotate(question, SpotDecision.CantVerify("The street list of your sector is not available.", reading));
		}

		var street = context.Sector.FindStreet(surface.FrenchName, surface.DutchName);
		IReadOnlyList<RuleSet> sets = street is null ? [] : street.RuleSetsFor(surface.Municipality, context.Tolerances);
		if (sets.Count == 0)
		{
			return Annotate(question, NotCovered(zoneCase.Type, reading, $"{where} ({NameNormalizer.TitleCase(surface.Municipality)}) isn't in your sector {context.Sector.Definition.Name}{ToleranceHint(street, surface)}."));
		}

		if (context.ListProblem is { } problem)
		{
			return Annotate(question, SpotDecision.CantVerify(problem, reading));
		}

		var ruleText = string.Join("; ", sets.Select(set => $"{set.Label}: odd {set.Odd.Text}, even {set.Even.Text}"));
		if (sets.Any(set => set.Odd.IsAll && set.Even.IsAll))
		{
			return Annotate(question, SpotDecision.Create(Verdict.Free, $"{where}: your card covers the whole street.", reading));
		}

		if (RuleSet.FindCondition(sets, StreetSide.Odd) is not null && RuleSet.FindCondition(sets, StreetSide.Even) is { } condition)
		{
			return Annotate(question, SpotDecision.Create(Verdict.CheckSigns, $"{where} is in your sector only under a condition: “{condition.Text}”.", reading));
		}

		var match = new StreetMatch(street!, sets, ruleText);
		return new DecisionStep.NeedAddresses(box, points => points.IsFailure
			? Annotate(question, SpotDecision.CantVerify($"Could not get the official house numbers ({points.Error.Message}). {ruleText}.", reading))
			: Annotate(question, DecideByHouseNumber(zoneCase, match, points.Value)));
	}

	private static string ToleranceHint(StreetRules? street, ZoneSurface surface)
	{
		var commune = NameNormalizer.Normalize(surface.Municipality);
		return street is not null && street.Tolerances.Any(t => NameNormalizer.Normalize(t.Commune) == commune)
			? " (only with a tolerance code your card does not have)"
			: string.Empty;
	}

	private static SpotDecision DecideByHouseNumber(ZoneCase zoneCase, StreetMatch match, IReadOnlyList<AddressPoint> points)
	{
		var question = zoneCase.Question;
		var reading = zoneCase.Reading;
		var surface = reading.Surface;
		var commune = NameNormalizer.Normalize(surface.Municipality);
		var mine = AddressAssigner.Assign(points, zoneCase.Zones, question.Context.Sector)
			.Where(a => a.StreetIndex == match.Street.Index && a.Commune == commune)
			.ToList();
		var (best, bestMetres) = NearestAddress(mine, question.Point);
		if (best is null)
		{
			return SpotDecision.CantVerify($"Your card covers only part of {surface.FrenchName} ({match.RuleText}). Look at the house number in front of you.", reading);
		}

		var front = $"In front of {match.Street.FrenchName} {best.Text}";
		var condition = RuleSet.FindCondition(match.Sets, best.House.Side);
		if (condition is not null && RuleSet.FindCovering(match.Sets, best.House) is null)
		{
			return SpotDecision.Create(Verdict.CheckSigns, $"{front}: this side is in your sector only under a condition: “{condition.Text}”.", reading);
		}

		var limit = LimitBetweenNumbers(zoneCase, match, mine, bestMetres);
		if (limit is not null)
		{
			return limit;
		}

		var covering = RuleSet.FindCovering(match.Sets, best.House);
		if (covering is null)
		{
			return NotCovered(zoneCase.Type, reading, $"{front}. Your card doesn't cover this number ({match.RuleText}).");
		}

		var label = covering.Label == "your sector" ? string.Empty : $" ({covering.Label})";
		return SpotDecision.Create(Verdict.Free, $"{front}. Your card covers this side of the street{label}.", reading);
	}

	private static SpotDecision? LimitBetweenNumbers(ZoneCase zoneCase, StreetMatch match, IReadOnlyList<AssignedAddress> mine, double bestMetres)
	{
		var question = zoneCase.Question;
		var margin = Math.Max(CardScopeEvaluator.BorderMarginMetres, question.AccuracyMetres ?? 0);
		var close = mine.Where(a => GeoMath.Distance(question.Point, a.Position) <= bestMetres + margin).ToList();
		var covered = close.Where(a => RuleSet.FindCovering(match.Sets, a.House) is not null).ToList();
		var notCovered = close.Where(a => RuleSet.FindCovering(match.Sets, a.House) is null).ToList();
		return covered.Count > 0 && notCovered.Count > 0
			? SpotDecision.Create(
				Verdict.CheckNumber,
				$"You're at a limit on {zoneCase.Reading.Surface.FrenchName}: free in front of {NumberList(covered)}, pay in front of {NumberList(notCovered)}.",
				zoneCase.Reading)
			: null;
	}

	private static (AssignedAddress? Address, double Metres) NearestAddress(IReadOnlyList<AssignedAddress> addresses, GeoPoint point)
	{
		AssignedAddress? nearest = null;
		var nearestMetres = double.PositiveInfinity;
		foreach (var address in addresses)
		{
			var distance = GeoMath.Distance(point, address.Position);
			if (distance < nearestMetres)
			{
				nearestMetres = distance;
				nearest = address;
			}
		}

		return (nearest, nearestMetres);
	}

	private static string NumberList(IEnumerable<AssignedAddress> addresses)
	{
		return string.Join(", ", addresses.Select(a => a.Text).Distinct(StringComparer.Ordinal).Take(4));
	}

	private static DecisionStep Annotate(SpotQuestion question, SpotDecision decision) => SpotAnnotator.Begin(question, decision);
	#endregion
}
