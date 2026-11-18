using System.Globalization;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds the card line with the rules of the first version of the screen: where the card is valid, whether it is valid
///              today, a warning with a renewal e-mail 30 days before it expires, and a warning when the City changed its official list.
/// </summary>
public static class CardLinePresenter
{
	#region Constants
	/// <summary>
	/// How many days before the end of the card the expiry warning starts.
	/// </summary>
	public const int ExpiryWarningDays = 30;
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the card line.
	/// </summary>
	/// <param name="card">The saved card, or <see langword="null"/> when there is none.</param>
	/// <param name="municipality">The municipality of the card, or <see langword="null"/> when there is no card.</param>
	/// <param name="sectorName">The name of the City sector of the card, or <see langword="null"/>.</param>
	/// <param name="officialList">The state of the official City document, or <see langword="null"/> when it does not apply.</param>
	/// <param name="today">Today's date on the clock of the device.</param>
	/// <returns>The view of the card line.</returns>
	public static CardLineView Present(ParkingCard? card, Municipality? municipality, string? sectorName, OfficialListStatus? officialList, DateOnly today)
	{
		if (card is null || municipality is null)
		{
			return new CardLineView(CardDot.None, "No card yet", string.Empty, false, null, true);
		}

		var state = CardState.Evaluate(card.Period, today);
		var listChanged = officialList?.State == OfficialListState.Changed;
		var (detail, warning, renewal) = Detail(card, state, listChanged, today);
		var dot = state.IsValid && !listChanged ? CardDot.Ok : CardDot.Bad;
		return new CardLineView(dot, Pill(card, municipality, sectorName), detail, warning, renewal, false);
	}

	/// <summary>
	/// Builds the card line for a saved card that cannot be read: it is not "no card yet", it asks the user to enter the card again.
	/// </summary>
	/// <returns>The view of the card line, with the link that adds a card.</returns>
	public static CardLineView Unreadable()
	{
		return new CardLineView(CardDot.Bad, "Card not readable", SavedDataUnreadableException.CardMessage, true, null, true);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the right-hand text: the official-list warning first, then why the card is not valid, then the expiry warning, then the end date.
	/// </summary>
	/// <param name="card">The saved card.</param>
	/// <param name="state">Whether the card is valid today.</param>
	/// <param name="listChanged">Whether the City changed its official list.</param>
	/// <param name="today">Today's date.</param>
	/// <returns>The text, whether it is a warning, and the renewal e-mail when one is offered.</returns>
	private static (string Detail, bool Warning, Uri? Renewal) Detail(ParkingCard card, CardState state, bool listChanged, DateOnly today)
	{
		if (listChanged)
		{
			return ("City list changed — update the app", true, null);
		}

		if (!state.IsValid)
		{
			return (state.Reason ?? string.Empty, true, null);
		}

		var days = card.Period.ValidTo is { } to ? to.DayNumber - today.DayNumber : (int?)null;
		return days is { } left && left <= ExpiryWarningDays
			? (string.Create(CultureInfo.InvariantCulture, $"Expires in {left} day(s)"), true, NavigationLinks.Renewal(card.Number))
			: ($"Valid until {ClockText.Date(card.Period.ValidTo)}", false, null);
	}

	/// <summary>
	/// Builds the text of the pill: where the card is valid, an Ixelles mark for tolerance codes, and the plate.
	/// </summary>
	/// <param name="card">The saved card.</param>
	/// <param name="municipality">The municipality of the card.</param>
	/// <param name="sectorName">The name of the City sector, or <see langword="null"/>.</param>
	/// <returns>The pill text.</returns>
	private static string Pill(ParkingCard card, Municipality municipality, string? sectorName)
	{
		var where = municipality.Kind switch
		{
			MunicipalityKind.Streets => sectorName ?? card.Sector ?? string.Empty,
			MunicipalityKind.Mailles => municipality.Name + Areas(card.Mailles.Count),
			_ => municipality.Name,
		};
		var tolerances = card.Tolerances.Count > 0 ? " + Ixelles" : string.Empty;
		var plate = string.IsNullOrEmpty(card.Plate) ? string.Empty : " · " + card.Plate;
		return where + tolerances + plate;
	}

	/// <summary>
	/// Counts the areas of a card in words.
	/// </summary>
	/// <param name="count">The number of areas ticked on the card.</param>
	/// <returns>The words that follow the municipality name.</returns>
	private static string Areas(int count)
	{
		if (count == 0)
		{
			return " · choose your areas";
		}

		var plural = count > 1 ? "s" : string.Empty;
		return string.Create(CultureInfo.InvariantCulture, $" · {count} area{plural}");
	}
	#endregion
}
