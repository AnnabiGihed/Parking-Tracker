using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Persistence.Device.Features.Shared.Encryption;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the question "does the database hold any encrypted value?". The cipher asks it before it creates a key: when the
///              protected storage holds no key but encrypted values exist, the key was lost, and a new key would leave those values
///              unreadable behind a silent replacement.
/// </summary>
public interface IEncryptedDataProbe
{
	#region Methods
	/// <summary>
	/// Tells whether any row holds an encrypted value (a card number or plate, a saved place or a parked car).
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with whether such a row exists, or the failure <see cref="Database.DeviceDatabaseErrors.ReadFailed"/> when the
	/// database cannot be read; "unknown" must never count as "none".</returns>
	Task<Result<bool>> AnyAsync(CancellationToken cancellationToken = default);
	#endregion
}
