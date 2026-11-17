using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to what the user saved on the phone: the card, the parked car, the map layer switches, whether the welcome
///              screen was seen and when the location banner was postponed. The implementation keeps the JSON shapes of the first
///              version of the app (<c>pt.card</c>, <c>pt.car</c>, <c>pt.layers</c>, <c>pt.permSnooze</c>), so that saved values stay readable.
/// </summary>
public interface ISettingsStore
{
	#region Methods
	/// <summary>
	/// Gets the saved residents' card.
	/// </summary>
	/// <returns>The card, or <see langword="null"/> when none is saved.</returns>
	ParkingCard? GetCard();

	/// <summary>
	/// Saves the residents' card, replacing the saved one.
	/// </summary>
	/// <param name="card">The card to keep.</param>
	void SaveCard(ParkingCard card);

	/// <summary>
	/// Removes the saved residents' card.
	/// </summary>
	void ClearCard();

	/// <summary>
	/// Gets the saved position of the parked car.
	/// </summary>
	/// <returns>The car, or <see langword="null"/> when none is saved.</returns>
	ParkedCar? GetCar();

	/// <summary>
	/// Saves the position of the parked car, replacing the saved one.
	/// </summary>
	/// <param name="car">The car to keep.</param>
	void SaveCar(ParkedCar car);

	/// <summary>
	/// Forgets the parked car.
	/// </summary>
	void ClearCar();

	/// <summary>
	/// Gets the map layer switches.
	/// </summary>
	/// <returns>The saved switches; the defaults when none are saved.</returns>
	LayerSettings GetLayers();

	/// <summary>
	/// Saves the map layer switches.
	/// </summary>
	/// <param name="layers">The switches to keep.</param>
	void SaveLayers(LayerSettings layers);

	/// <summary>
	/// Tells whether the welcome screen was already shown.
	/// </summary>
	/// <returns><see langword="true"/> when the user has seen the welcome screen.</returns>
	bool GetWelcomeSeen();

	/// <summary>
	/// Remembers that the welcome screen was shown.
	/// </summary>
	void SaveWelcomeSeen();

	/// <summary>
	/// Gets until when the user postponed the "allow location all the time" banner.
	/// </summary>
	/// <returns>The moment, or <see langword="null"/> when it was not postponed.</returns>
	DateTimeOffset? GetPermissionSnoozeUntil();

	/// <summary>
	/// Postpones the "allow location all the time" banner.
	/// </summary>
	/// <param name="until">The moment the banner may show again.</param>
	void SavePermissionSnoozeUntil(DateTimeOffset until);
	#endregion
}
