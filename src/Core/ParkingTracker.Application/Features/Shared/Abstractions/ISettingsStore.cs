using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to what the user saved on the phone: the card, the parked car, the parking timer, the parking alerts switch,
///              the map layer switches, whether the welcome screen was seen and when the location banner was postponed. The calls
///              are asynchronous because the encrypted storage of the phone is. The implementation keeps the JSON shapes of the
///              first version of the app (<c>pt.card</c>, <c>pt.car</c>, <c>pt.timer</c>, <c>pt.bgAlerts</c>, <c>pt.layers</c>,
///              <c>pt.permSnooze</c>), so that saved values stay readable.
/// </summary>
public interface ISettingsStore
{
	#region Methods
	/// <summary>
	/// Gets the saved residents' card.
	/// </summary>
	/// <returns>A task with the card, or <see langword="null"/> when none is saved.</returns>
	/// <exception cref="SavedDataUnreadableException">A card is saved but cannot be read; that is not "no card".</exception>
	Task<ParkingCard?> GetCardAsync();

	/// <summary>
	/// Saves the residents' card, replacing the saved one.
	/// </summary>
	/// <param name="card">The card to keep.</param>
	/// <returns>A task that completes when the card is saved.</returns>
	Task SaveCardAsync(ParkingCard card);

	/// <summary>
	/// Removes the saved residents' card.
	/// </summary>
	/// <returns>A task that completes when the card is removed.</returns>
	Task ClearCardAsync();

	/// <summary>
	/// Gets the saved position of the parked car.
	/// </summary>
	/// <returns>A task with the car, or <see langword="null"/> when none is saved.</returns>
	/// <exception cref="SavedDataUnreadableException">A car is saved but cannot be read.</exception>
	Task<ParkedCar?> GetCarAsync();

	/// <summary>
	/// Saves the position of the parked car, replacing the saved one.
	/// </summary>
	/// <param name="car">The car to keep.</param>
	/// <returns>A task that completes when the car is saved.</returns>
	Task SaveCarAsync(ParkedCar car);

	/// <summary>
	/// Forgets the parked car.
	/// </summary>
	/// <returns>A task that completes when the car is forgotten.</returns>
	Task ClearCarAsync();

	/// <summary>
	/// Gets the saved parking timer.
	/// </summary>
	/// <returns>A task with the timer, or <see langword="null"/> when none is saved.</returns>
	/// <exception cref="SavedDataUnreadableException">A session that holds the timer is saved but cannot be read.</exception>
	Task<ParkingTimer?> GetTimerAsync();

	/// <summary>
	/// Saves the parking timer, replacing the saved one.
	/// </summary>
	/// <param name="timer">The timer to keep.</param>
	/// <returns>A task that completes when the timer is saved.</returns>
	Task SaveTimerAsync(ParkingTimer timer);

	/// <summary>
	/// Forgets the parking timer.
	/// </summary>
	/// <returns>A task that completes when the timer is forgotten.</returns>
	Task ClearTimerAsync();

	/// <summary>
	/// Tells whether the parking alerts are on.
	/// </summary>
	/// <returns>A task with <see langword="true"/> unless the user switched the alerts off; they are on by default.</returns>
	Task<bool> GetAlertsEnabledAsync();

	/// <summary>
	/// Saves the parking alerts switch.
	/// </summary>
	/// <param name="enabled">Whether the alerts are on.</param>
	/// <returns>A task that completes when the switch is saved.</returns>
	Task SaveAlertsEnabledAsync(bool enabled);

	/// <summary>
	/// Gets the map layer switches.
	/// </summary>
	/// <returns>A task with the saved switches; the defaults when none are saved.</returns>
	Task<LayerSettings> GetLayersAsync();

	/// <summary>
	/// Saves the map layer switches.
	/// </summary>
	/// <param name="layers">The switches to keep.</param>
	/// <returns>A task that completes when the switches are saved.</returns>
	Task SaveLayersAsync(LayerSettings layers);

	/// <summary>
	/// Tells whether the welcome screen was already shown.
	/// </summary>
	/// <returns>A task with <see langword="true"/> when the user has seen the welcome screen.</returns>
	Task<bool> GetWelcomeSeenAsync();

	/// <summary>
	/// Remembers that the welcome screen was shown.
	/// </summary>
	/// <returns>A task that completes when it is saved.</returns>
	Task SaveWelcomeSeenAsync();

	/// <summary>
	/// Gets until when the user postponed the "allow location all the time" banner.
	/// </summary>
	/// <returns>A task with the moment, or <see langword="null"/> when it was not postponed.</returns>
	Task<DateTimeOffset?> GetPermissionSnoozeUntilAsync();

	/// <summary>
	/// Postpones the "allow location all the time" banner.
	/// </summary>
	/// <param name="until">The moment the banner may show again.</param>
	/// <returns>A task that completes when the moment is saved.</returns>
	Task SavePermissionSnoozeUntilAsync(DateTimeOffset until);
	#endregion
}
