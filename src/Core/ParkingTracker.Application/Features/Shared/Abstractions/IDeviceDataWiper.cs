using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that removes the data of an account from the phone (ADR-0032). Each method is one transaction: either every
///              table named is emptied, or none is.
/// </summary>
public interface IDeviceDataWiper
{
	#region Methods
	/// <summary>
	/// Removes the synced profile: the home, work and favourite places, the vehicle, the residents' card and the sync state. The
	/// parking session and the owner binding stay.
	/// </summary>
	/// <param name="cancellationToken">Cancels the wipe before it starts.</param>
	/// <returns>A task with success, or the failure of the wipe; nothing was removed on a failure.</returns>
	Task<Result> WipeProfileAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes everything that belongs to the account: the profile as in <see cref="WipeProfileAsync"/>, the parking session and the
	/// owner binding.
	/// </summary>
	/// <param name="cancellationToken">Cancels the wipe before it starts.</param>
	/// <returns>A task with success, or the failure of the wipe; nothing was removed on a failure.</returns>
	Task<Result> WipeAllAsync(CancellationToken cancellationToken = default);
	#endregion
}
