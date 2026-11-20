using ParkingTracker.Application.Features.ParkingSessions.Abstractions;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;

namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The cleanup that every store of the device database runs when the key of the encrypted fields was lost: the rows that the
///              lost key left unreadable (the card, the parking session and the saved places) are removed, the plate of the vehicle (its only
///              encrypted field) is cleared and the rest of the vehicle, which is still readable, is kept; the change is committed in the
///              scope of the write, so that the new key can be created, and then the other stores are told to forget what they remembered. The store that found the loss stages its own value again afterwards, in the same scope.
/// </summary>
public static class LostKeyCleanup
{
	#region Public Methods
	/// <summary>
	/// Removes every encrypted row and the encrypted plate of the vehicle, commits the change and raises <see cref="DeviceDataResetNotice.StaleRowsRemoved"/>.
	/// </summary>
	/// <param name="scope">The scope of the write that found the lost key.</param>
	/// <param name="cards">The repository of the residents' card.</param>
	/// <param name="sessions">The repository of the parking session.</param>
	/// <param name="places">The repository of the saved places.</param>
	/// <param name="vehicles">The repository of the vehicle profile.</param>
	/// <param name="resetNotice">Tells the other stores that the stale rows were removed.</param>
	/// <param name="cancellationToken">Cancels the commit.</param>
	/// <returns>A task that completes when the rows are removed and the stores were told.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The database refused the removal.</exception>
	public static async Task RemoveStaleRowsAsync(
		IDeviceChangeScope scope,
		IResidentCardRepository cards,
		IParkingSessionRepository sessions,
		ISavedPlacesRepository places,
		IVehicleRepository vehicles,
		DeviceDataResetNotice resetNotice,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scope);
		ArgumentNullException.ThrowIfNull(cards);
		ArgumentNullException.ThrowIfNull(sessions);
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(vehicles);
		ArgumentNullException.ThrowIfNull(resetNotice);
		cards.Remove(scope);
		sessions.Remove(scope);
		places.Remove(scope);
		vehicles.ClearPlate(scope);
		var committed = await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
		if (committed.IsFailure)
		{
			throw new InvalidOperationException(committed.Error.Message);
		}

		resetNotice.Raise();
	}
	#endregion
}
