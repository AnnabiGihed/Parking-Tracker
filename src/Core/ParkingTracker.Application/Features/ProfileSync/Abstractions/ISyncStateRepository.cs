using ParkingTracker.Application.Features.Shared.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the <c>sync_state</c> row of the device database (ADR-0032). Writes are staged in the scope the caller holds, so
///              that the row changes in the same transaction as the data it describes.
/// </summary>
public interface ISyncStateRepository
{
	#region Methods
	/// <summary>
	/// Reads the sync state.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the state; <see cref="SyncState.Empty"/> when there is no row, or the failure of the read.</returns>
	Task<Result<SyncState>> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the moment of a change of one section and sets the changed flag, creating the row when it is missing.
	/// </summary>
	/// <param name="scope">The scope of the data change.</param>
	/// <param name="section">The section that changed.</param>
	/// <param name="at">The moment of the change.</param>
	void StageTouch(IDeviceChangeScope scope, SyncSection section, DateTimeOffset at);

	/// <summary>
	/// Stages the whole state, creating the row when it is missing. The pending-removal flag is left as it is.
	/// </summary>
	/// <param name="scope">The scope of the change.</param>
	/// <param name="state">The state to store.</param>
	void StageState(IDeviceChangeScope scope, SyncState state);
	#endregion
}
