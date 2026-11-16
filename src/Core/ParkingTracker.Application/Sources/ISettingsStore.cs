using ParkingTracker.Domain.Cards;

namespace ParkingTracker.Application.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to what the user saved on the phone. Only the read side needed by the decisions exists so far; the card
///              storage and the other settings are added with the app shell.
/// </summary>
public interface ISettingsStore
{
	#region Methods
	/// <summary>
	/// Gets the saved residents' card.
	/// </summary>
	/// <returns>The card, or <see langword="null"/> when none is saved.</returns>
	ParkingCard? GetCard();
	#endregion
}
