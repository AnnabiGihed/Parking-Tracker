using System.Globalization;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.Services;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns a spot decision into what the status block and the chips show, with the headlines, icons, colours and
///              rules of the first version of the screen. Pure code: the decision logic stays in the Domain.
/// </summary>
public static class SpotStatusPresenter
{
	#region Constants
	/// <summary>
	/// The colour of a zone chip when the zone colour is not known.
	/// </summary>
	private const string UnknownZoneColour = "#94a3b8";
	#endregion

	#region Static Instances
	/// <summary>
	/// The icon of each answer title; the headline itself is <see cref="SpotDecision.Headline"/>. A title that is not listed gets the help icon.
	/// </summary>
	private static readonly Dictionary<string, string> Icons = new(StringComparer.Ordinal)
	{
		["FREE ✓"] = "check",
		["FREE NOW"] = "check",
		["PAY"] = "euro",
		["RESERVED"] = "block",
		["CHECK THE NUMBER"] = "warning",
		["TIME LIMITED"] = "timer",
		["PARKING DISC"] = "timer",
		["CAN'T VERIFY"] = "help",
	};

	/// <summary>
	/// The colour of the dot of each zone colour code.
	/// </summary>
	private static readonly Dictionary<int, string> ZoneColours = new()
	{
		[0] = "#0d9488",
		[1] = "#7f1d1d",
		[2] = "#f97316",
		[3] = "#94a3b8",
		[4] = "#22c55e",
		[5] = "#2563eb",
		[6] = "#a855f7",
		[9] = "#0f766e",
	};
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the status block for a decision.
	/// </summary>
	/// <param name="decision">The decision from the Domain.</param>
	/// <returns>The view of the status block.</returns>
	public static SpotStatusView Present(SpotDecision decision)
	{
		ArgumentNullException.ThrowIfNull(decision);
		var icon = Icons.TryGetValue(decision.Title, out var known) ? known : "help";
		var isFree = decision.Status == SpotStatus.Free;
		var showTariff = decision.Tariff is not null && (!isFree || decision.Verdict == Verdict.FreeNow);
		return new SpotStatusView(
			decision.Headline,
			icon,
			decision.Detail,
			ColourKey(decision.Status),
			decision.Notes,
			showTariff ? decision.Tariff : null,
			isFree,
			decision.Status == SpotStatus.Pay && decision.Zone is not null && decision.ReservedSpace is null,
			false);
	}

	/// <summary>
	/// Gets the headline of a decision, as saved with a parked car.
	/// </summary>
	/// <param name="decision">The decision from the Domain.</param>
	/// <returns>The headline, for example <c>Free for you here</c>.</returns>
	public static string HeadlineOf(SpotDecision decision)
	{
		ArgumentNullException.ThrowIfNull(decision);
		return decision.Headline;
	}

	/// <summary>
	/// Builds the chips under the status block: when and how the answer was read, where the position came from, and the zone.
	/// </summary>
	/// <param name="decision">The decision from the Domain.</param>
	/// <param name="source">Whether the place is the user's position or a pin.</param>
	/// <param name="accuracyMetres">The accuracy of the position in metres, or <see langword="null"/>.</param>
	/// <param name="now">The moment on the clock of the device, written on the first chip.</param>
	/// <returns>The chips, in the order they are shown.</returns>
	public static IReadOnlyList<StatusChip> Chips(SpotDecision decision, CheckSource source, double? accuracyMetres, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(decision);
		var live = decision.Zone is not null;
		List<StatusChip> chips = [new($"{(live ? "Live" : "Checked")} · {ClockText.Time(now)}", "live", live ? ChipKind.Live : ChipKind.Plain)];
		chips.Add(SourceChip(source, accuracyMetres));
		if (decision.Zone is { } zone)
		{
			chips.Add(ZoneChip(zone));
		}

		return chips;
	}

	/// <summary>
	/// Builds the chip shown while a check is running.
	/// </summary>
	/// <param name="text">What the app is waiting for.</param>
	/// <returns>The chip with a spinner.</returns>
	public static StatusChip Busy(string text) => new(text, string.Empty, ChipKind.Busy);
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the chip that says where the position came from.
	/// </summary>
	/// <param name="source">Whether the place is the user's position or a pin.</param>
	/// <param name="accuracyMetres">The accuracy of the position in metres, or <see langword="null"/>.</param>
	/// <returns>The source chip.</returns>
	private static StatusChip SourceChip(CheckSource source, double? accuracyMetres)
	{
		if (source == CheckSource.Pin)
		{
			return new StatusChip("Pin", "pin", ChipKind.Plain);
		}

		var accuracy = accuracyMetres is { } metres ? string.Create(CultureInfo.InvariantCulture, $" ±{Math.Round(metres, MidpointRounding.AwayFromZero)} m") : string.Empty;
		return new StatusChip("My position" + accuracy, "navigation", ChipKind.Plain);
	}

	/// <summary>
	/// Gets the CSS class that colours the status block.
	/// </summary>
	/// <param name="status">The status of the decision.</param>
	/// <returns>The class name.</returns>
	private static string ColourKey(SpotStatus status) => status switch
	{
		SpotStatus.Free => "free",
		SpotStatus.Pay => "pay",
		_ => "unknown",
	};

	/// <summary>
	/// Builds the chip of the zone: its colour name and the municipality in lower case with a capital first letter.
	/// </summary>
	/// <param name="zone">The zone the place is in.</param>
	/// <returns>The zone chip.</returns>
	private static StatusChip ZoneChip(ZoneReading zone)
	{
		var municipality = zone.Surface.Municipality.ToLowerInvariant();
		var capitalised = municipality.Length == 0 ? municipality : char.ToUpperInvariant(municipality[0]) + municipality[1..];
		var colour = zone.Surface.ColourCode is { } code && ZoneColours.TryGetValue(code, out var known) ? known : UnknownZoneColour;
		return new StatusChip($"{ZoneNames.Of(zone.Surface.ColourCode) ?? "unknown"} zone · {capitalised}", string.Empty, ChipKind.Zone, colour);
	}
	#endregion
}
