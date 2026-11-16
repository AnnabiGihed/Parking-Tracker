using System.Globalization;

namespace ParkingTracker.Domain.Cards;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Whether the user's residents' card can be used today, and when not, why, in words for the user.
/// </summary>
/// <param name="IsValid">Whether the card is set up and valid today.</param>
/// <param name="Reason">Why the card cannot be used; <see langword="null"/> when it is valid.</param>
public sealed record CardState(bool IsValid, string? Reason)
{
	#region Factory Methods
	/// <summary>
	/// Checks a card against today's date, taken from the local clock of the <see cref="TimeProvider"/>.
	/// </summary>
	/// <param name="card">The card's validity dates, or <see langword="null"/> when no card is set up.</param>
	/// <param name="timeProvider">The clock.</param>
	/// <returns>Invalid with a reason when there is no card, it is not valid yet or it has expired; otherwise valid.</returns>
	public static CardState Evaluate(CardPeriod? card, TimeProvider timeProvider)
	{
		ArgumentNullException.ThrowIfNull(timeProvider);
		if (card is null)
		{
			return new CardState(false, "No parking card set up yet — tap 🪪.");
		}

		var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
		if (card.ValidFrom is { } from && today < from)
		{
			return new CardState(false, $"Your card is only valid from {Format(from)}.");
		}

		if (card.ValidTo is { } to && today > to)
		{
			return new CardState(false, $"Your card expired on {Format(to)}.");
		}

		return new CardState(true, null);
	}
	#endregion

	#region Private Helpers
	private static string Format(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
	#endregion
}
