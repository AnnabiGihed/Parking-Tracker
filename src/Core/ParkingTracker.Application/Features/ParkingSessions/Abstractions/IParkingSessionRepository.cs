using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.ParkingSessions.Aggregates;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.ParkingSessions.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the parking session on the device. The phone holds one open session, so the port loads "the" session and a
///              change replaces whatever is stored; the session keeps its identifier across loads. Changes are staged in the
///              <see cref="ParkingTracker.Application.Features.Shared.Abstractions.IDeviceChangeScope"/> of the operation and applied
///              when the scope commits.
/// </summary>
public interface IParkingSessionRepository
{
	#region Methods
	/// <summary>
	/// Loads the stored session, rebuilt through <see cref="ParkingSession.Restore"/> so that a stored session that breaks a rule is
	/// refused.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the session; the failure <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.NotStored"/>
	/// when none is stored; <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.KeyUnavailable"/> when the
	/// key cannot be read now (the session may exist); or another failure when the position cannot be decrypted, the stored session
	/// breaks a rule or the database fails.</returns>
	Task<Result<ParkingSession>> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages a new session for the next commit; nothing happens at commit when a session is stored already, so that a newer one is never overwritten.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	/// <param name="session">The session to store.</param>
	/// <param name="cancellationToken">Cancels the encryption of the position.</param>
	/// <returns>A task with success, or a failure when the position cannot be encrypted.</returns>
	Task<Result> AddAsync(IDeviceChangeScope scope, ParkingSession session, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the replacement of the stored session by <paramref name="session"/> for the next commit.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	/// <param name="session">The session that replaces the stored one.</param>
	/// <param name="cancellationToken">Cancels the encryption of the position.</param>
	/// <returns>A task with success, or a failure when the position cannot be encrypted.</returns>
	Task<Result> UpdateAsync(IDeviceChangeScope scope, ParkingSession session, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the removal of the stored session for the next commit.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	void Remove(IDeviceChangeScope scope);
	#endregion
}
