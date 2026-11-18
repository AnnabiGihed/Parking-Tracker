using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads of the settings store for the callers that only decide what to show and never give a verdict about a spot:
///              a card, car or timer that cannot be read counts as "none" for them, so that the welcome screen, the map layers and
///              the alarm restore keep working. The spot check, the parked-car alert and the card line do not use these: they call
///              the port itself and must tell the user that the saved data cannot be read (<see cref="SavedDataUnreadableException"/>).
/// </summary>
public static class SettingsStoreExtensions
{
	#region Public Methods
	/// <summary>
	/// Gets the saved card, or <see langword="null"/> when none is saved or it cannot be read.
	/// </summary>
	/// <param name="settings">The settings store.</param>
	/// <returns>A task with the card, or <see langword="null"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
	public static async Task<ParkingCard?> GetCardOrNoneAsync(this ISettingsStore settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		try
		{
			return await settings.GetCardAsync().ConfigureAwait(false);
		}
		catch (SavedDataUnreadableException)
		{
			return null;
		}
	}

	/// <summary>
	/// Gets the saved parked car, or <see langword="null"/> when none is saved or it cannot be read.
	/// </summary>
	/// <param name="settings">The settings store.</param>
	/// <returns>A task with the car, or <see langword="null"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
	public static async Task<ParkedCar?> GetCarOrNoneAsync(this ISettingsStore settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		try
		{
			return await settings.GetCarAsync().ConfigureAwait(false);
		}
		catch (SavedDataUnreadableException)
		{
			return null;
		}
	}

	/// <summary>
	/// Gets the saved timer, or <see langword="null"/> when none is saved or it cannot be read.
	/// </summary>
	/// <param name="settings">The settings store.</param>
	/// <returns>A task with the timer, or <see langword="null"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
	public static async Task<ParkingTimer?> GetTimerOrNoneAsync(this ISettingsStore settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		try
		{
			return await settings.GetTimerAsync().ConfigureAwait(false);
		}
		catch (SavedDataUnreadableException)
		{
			return null;
		}
	}
	#endregion
}
