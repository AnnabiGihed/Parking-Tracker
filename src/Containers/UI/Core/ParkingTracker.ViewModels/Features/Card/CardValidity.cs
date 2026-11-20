namespace ParkingTracker.ViewModels.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The rule that proposes the last day of validity of a residents' card once its first day is known.
/// </summary>
public static class CardValidity
{
	#region Public Methods
	/// <summary>
	/// Proposes the last day of validity: the same day one year later, or the last day of February when the first day is 29 February
	/// and the next year is not a leap year.
	/// </summary>
	/// <param name="from">The first day of validity.</param>
	/// <returns>The proposed last day of validity.</returns>
	public static DateOnly SuggestValidTo(DateOnly from) => from.AddYears(1);
	#endregion
}
