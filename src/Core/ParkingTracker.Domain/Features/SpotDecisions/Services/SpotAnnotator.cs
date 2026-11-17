using ParkingTracker.Domain.Features.PaidHours.Services;
using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Shared.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Domain.Shared;
using PaidHoursCalculator = ParkingTracker.Domain.Features.PaidHours.Services.PaidHours;

namespace ParkingTracker.Domain.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The second half of a decision, applied to the answer of <see cref="SpotDecider"/>: reserved spaces at the spot
///              (whatever the card), time-limited zones, roadworks and events, and the paid hours of the zone, which turn
///              "pay" into "free now" outside them. Like <see cref="SpotDecider"/> it asks for live data through
///              <see cref="DecisionStep"/> values.
/// </summary>
public static class SpotAnnotator
{
	#region Constants
	/// <summary>
	/// How far from the place, in degrees of latitude, spaces and roadworks are fetched.
	/// </summary>
	public const double SpotBoxDelta = 0.0004;

	/// <summary>
	/// A reserved space this close, in metres, is the one the user is on.
	/// </summary>
	public const double OnSpaceMetres = 3;

	/// <summary>
	/// The smallest distance, in metres, within which spaces are looked at.
	/// </summary>
	public const double MinimumReachMetres = 4;

	/// <summary>
	/// The GPS accuracy, in metres, up to which the user is taken to be exactly on a reserved space.
	/// </summary>
	public const double PreciseAccuracyMetres = 6;
	#endregion

	#region Public Methods
	/// <summary>
	/// Continues a zone decision with reserved spaces, roadworks and paid hours.
	/// </summary>
	/// <param name="question">The card, the place and the moment.</param>
	/// <param name="decision">The answer of the zone decision.</param>
	/// <returns>The answer itself when there is no street surface to annotate; otherwise the request for the parking spaces.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static DecisionStep Begin(SpotQuestion question, SpotDecision decision)
	{
		ArgumentNullException.ThrowIfNull(question);
		ArgumentNullException.ThrowIfNull(decision);
		if (decision.Zone is null)
		{
			return new DecisionStep.Decided(decision);
		}

		var box = GeoBox.Around(question.Point, SpotBoxDelta);
		return new DecisionStep.NeedSpaces(box, spaces => OnSpaces(question, box, decision, spaces));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Continues the decision once the reserved spaces are known.
	/// </summary>
	/// <param name="question">The question being decided.</param>
	/// <param name="box">The bounding box.</param>
	/// <param name="decision">The decision so far.</param>
	/// <param name="spaces">Reserved spaces found in the area, or the failure.</param>
	/// <returns>The next step.</returns>
	private static DecisionStep OnSpaces(SpotQuestion question, GeoBox box, SpotDecision decision, Result<IReadOnlyList<ReservedSpace>> spaces)
	{
		var zone = decision.Zone!;
		if (spaces.IsFailure)
		{
			var onlyZone = new ZoneReading(zone.Surface, null, null);
			return new DecisionStep.Decided(SpotDecision.CantVerify($"Could not check reserved parking spaces ({spaces.Error.Message}).", onlyZone));
		}

		var accuracy = question.AccuracyMetres;
		var reach = Math.Max(MinimumReachMetres, accuracy ?? 0);
		var near = spaces.Value
			.Select(space => (Space: space, Metres: GeoMath.Distance(question.Point, space.Point)))
			.Where(entry => entry.Metres <= reach)
			.OrderBy(entry => entry.Metres)
			.ToList();
		var precise = accuracy is null or <= PreciseAccuracyMetres;
		if (near.Count > 0 && near[0].Space.IsReserved && !near[0].Space.IsTimeLimited && near[0].Metres <= OnSpaceMetres && precise)
		{
			var first = near[0].Space;
			return new DecisionStep.Decided(decision with
			{
				Verdict = Verdict.Reserved,
				Detail = $"{(string.IsNullOrEmpty(first.Label) ? "Reserved space" : first.Label)}: you can't park here, whatever your card. {decision.Detail}",
				ReservedSpace = first,
				Notes = [],
			});
		}

		var notes = SpaceNotes(near);
		var current = decision;
		if (near.FirstOrDefault(entry => entry.Space.IsTimeLimited && entry.Metres <= OnSpaceMetres) is { Space: not null } timed)
		{
			notes.Insert(0, $"{timed.Space.Label}: parking time is limited here, your card doesn't extend it.");
			current = decision.Verdict == Verdict.Free ? decision with { Verdict = Verdict.TimeLimited } : decision;
		}

		return new DecisionStep.NeedWorks(box, works => OnWorks(question, current, notes, works));
	}

	/// <summary>
	/// Builds the notes about the reserved spaces near the point.
	/// </summary>
	/// <param name="near">Reserved spaces within range, with their distance in metres.</param>
	/// <returns>The notes, one per kind of reserved space.</returns>
	private static List<string> SpaceNotes(List<(ReservedSpace Space, double Metres)> near)
	{
		return near
			.Where(entry => entry.Space.IsReserved && !entry.Space.IsTimeLimited)
			.GroupBy(entry => entry.Space.ReservationType)
			.Select(group => group.Last())
			.Select(entry => $"⚠️ {entry.Space.Label} {JsText.Round(entry.Metres)} m away — don't park on it.")
			.ToList();
	}

	/// <summary>
	/// Finishes the decision once the works are known.
	/// </summary>
	/// <param name="question">The question being decided.</param>
	/// <param name="decision">The decision so far.</param>
	/// <param name="notes">Notes gathered so far for the decision.</param>
	/// <param name="works">Roadworks and events found in the area, or the failure.</param>
	/// <returns>The next step.</returns>
	private static DecisionStep.Decided OnWorks(SpotQuestion question, SpotDecision decision, List<string> notes, Result<IReadOnlyList<WorkItem>> works)
	{
		if (works.IsSuccess)
		{
			foreach (var item in works.Value.Where(item => item.IsAt(question.Point)))
			{
				var place = string.IsNullOrEmpty(item.Where) ? string.Empty : " — " + item.Where;
				var until = string.IsNullOrEmpty(item.To) ? string.Empty : $" (until {JsText.FormatDate(item.To)})";
				notes.Add($"🚧 {item.Title}{place}{until}. Parking may be forbidden: check the signs.");
			}
		}
		else
		{
			notes.Add($"Could not check roadworks ({works.Error.Message}).");
		}

		var zone = decision.Zone!;
		if (!string.IsNullOrEmpty(zone.RuleError))
		{
			notes.Add($"Could not check the paid hours ({zone.RuleError}): the app assumes they apply.");
		}

		var result = decision;
		if (zone.Rule is { } rule && zone.Surface.ColourCode != 0)
		{
			result = decision with { Tariff = TariffText.Describe(rule) };
			if (NeedsPaying(result.Verdict) && !PaidHoursCalculator.IsPaidAt(rule, question.Now))
			{
				notes.Insert(0, $"During paid hours: {result.Detail}");
				result = result with { Verdict = Verdict.FreeNow, Detail = FreeNowDetail(rule, question.Now) };
			}
		}

		return new DecisionStep.Decided(result with { Notes = notes });
	}

	/// <summary>
	/// Determines whether the verdict means the driver has to pay.
	/// </summary>
	/// <param name="verdict">Verdict to test.</param>
	/// <returns><see langword="true"/> when the verdict means the driver has to pay; otherwise <see langword="false"/>.</returns>
	private static bool NeedsPaying(Verdict verdict)
	{
		return verdict is Verdict.Pay or Verdict.ParkingDisc or Verdict.CheckNumber or Verdict.CheckCard or Verdict.CheckSigns;
	}

	/// <summary>
	/// Builds the detail shown when parking is free right now but paid later.
	/// </summary>
	/// <param name="rule">The parking rule.</param>
	/// <param name="now">The current moment.</param>
	/// <returns>The detail text.</returns>
	private static string FreeNowDetail(ParkingRule rule, DateTimeOffset now)
	{
		var next = PaidHoursCalculator.NextPaid(rule, now);
		var holiday = BelgianHolidays.IsHoliday(BrusselsTime.At(now).Date) && !rule.PaidOnHolidays;
		var what = rule.Type == ZoneType.Disk ? "the parking disc" : "paid parking";
		return $"No need to pay right now: here, {what} applies {TariffText.Schedule(rule)}{(holiday ? " (not on public holidays)" : string.Empty)}.{(next is null ? string.Empty : $" It starts again {next}.")}";
	}
	#endregion
}
