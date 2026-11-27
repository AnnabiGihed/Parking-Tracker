using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the account that owns the data on the phone (ADR-0032). The phone keeps only the hash of the account, never the
///              account id itself (see <see cref="DeviceOwnerHash"/>); the sign-in flow compares it with the account that just signed in,
///              to tell the first account of the phone, the same account and another account apart. It also keeps the flag that the
///              server confirmed an account deletion whose removal from the phone is not finished, so that it is finished after a restart.
/// </summary>
public interface IDeviceOwnerStore
{
	#region Methods
	/// <summary>
	/// Reads the hash of the account that the data on the phone belongs to.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the hash, or the failure <see cref="DeviceDataErrors.NotStored"/> when no account is bound yet.</returns>
	Task<Result<string>> GetAccountHashAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Binds the data on the phone to an account, replacing a previous binding, and commits it.
	/// </summary>
	/// <param name="accountHash">The hash of the account, from <see cref="DeviceOwnerHash.Of"/>.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task with success, or the failure of the commit.</returns>
	Task<Result> BindAsync(string accountHash, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads whether the server confirmed the deletion of the account and the removal of its data from the phone did not finish.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with <see langword="true"/> when the removal is still pending, or the failure of the read.</returns>
	Task<Result<bool>> GetErasePendingAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the flag that the server confirmed the deletion of the account and the removal of its data from the phone is not finished,
	/// and commits it. The full wipe clears it by deleting the sync state row.
	/// </summary>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task with success, or the failure of the commit.</returns>
	Task<Result> MarkErasePendingAsync(CancellationToken cancellationToken = default);
	#endregion
}
