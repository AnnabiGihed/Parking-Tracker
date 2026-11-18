using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Persistence.Device.Features.Shared.Encryption;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to where the phone keeps the key of the field cipher. The host implements it over the platform's protected
///              storage; the key never goes into the database, into a backup or into a log. The two answers "this phone has no such
///              key" and "the storage cannot be read right now" are different failures, because the first means the data is
///              lost for good and the second that it is only out of reach. Reading a key never creates one: the cipher creates one
///              with <see cref="CreateCurrentAsync"/> only after it has checked that no encrypted row exists, because a new key behind
///              existing rows would leave them unreadable without anyone being told. An implementation never replaces a key it could not read.
/// </summary>
public interface IDeviceKeyStore
{
	#region Methods
	/// <summary>
	/// Gets the key that new values are encrypted with; it never creates one.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with the current key; the failure <see cref="Database.DeviceDatabaseErrors.KeyMissing"/> when the storage answered
	/// that it holds none; or <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.KeyUnavailable"/> when the
	/// storage cannot be read now.</returns>
	Task<Result<DeviceKey>> GetCurrentAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates the current key and keeps it, unless one exists by now (then that one is returned); call it only when no encrypted row exists.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with the key, or the failure <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.KeyUnavailable"/>
	/// when the storage cannot be read or written; a key that exists is never replaced then.</returns>
	Task<Result<DeviceKey>> CreateCurrentAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Finds the key that an encrypted value names; it never creates one.
	/// </summary>
	/// <param name="keyId">The identifier written in front of the value.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with the key; the failure <see cref="Database.DeviceDatabaseErrors.KeyMissing"/> when this phone has no such key, as after
	/// a restore on a new phone; or <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.KeyUnavailable"/> when the storage cannot be read now.</returns>
	Task<Result<DeviceKey>> FindAsync(string keyId, CancellationToken cancellationToken = default);
	#endregion
}
