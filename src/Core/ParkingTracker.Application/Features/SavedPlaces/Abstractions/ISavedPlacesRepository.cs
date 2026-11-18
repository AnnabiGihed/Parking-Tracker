using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SavedPlaces.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the list of places the user saved on the device. There is one list per device, so a change replaces the whole
///              stored list; the list and its places keep their identifiers across loads. Changes are staged in the
///              <see cref="ParkingTracker.Application.Features.Shared.Abstractions.IDeviceChangeScope"/> of the operation and applied
///              when the scope commits.
/// </summary>
public interface ISavedPlacesRepository
{
	#region Methods
	/// <summary>
	/// Loads the list, rebuilt through <c>SavedPlaces.Restore</c> so that stored places that break a rule are refused.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the list (empty when nothing is stored), or a failure:
	/// <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.KeyUnavailable"/> when the key cannot be read now,
	/// or another when a place cannot be decrypted, the stored places break a rule or the database fails. A place is never left out
	/// silently.</returns>
	Task<Result<ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces>> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the list as new for the next commit; nothing happens at commit when places are stored already, so that newer places are never overwritten.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	/// <param name="places">The list to store.</param>
	/// <param name="cancellationToken">Cancels the encryption of the coordinates.</param>
	/// <returns>A task with success, or a failure when the coordinates cannot be encrypted.</returns>
	Task<Result> AddAsync(IDeviceChangeScope scope, ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces places, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the replacement of the stored list by <paramref name="places"/> for the next commit.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	/// <param name="places">The list that replaces the stored one.</param>
	/// <param name="cancellationToken">Cancels the encryption of the coordinates.</param>
	/// <returns>A task with success, or a failure when the coordinates cannot be encrypted.</returns>
	Task<Result> UpdateAsync(IDeviceChangeScope scope, ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces places, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the removal of every stored place for the next commit.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	void Remove(IDeviceChangeScope scope);
	#endregion
}
