using ParkingTracker.Domain.Features.Accounts.ValueObjects;

namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the erasure log (architecture 6.1): the subject hashes of the accounts that were erased, kept so that an
///              erasure is applied again after a backup restore. A record is staged and saved by the unit of work together with
///              the deletion of the account, so one transaction holds both. The log is not a ban: the same person may sign in again.
/// </summary>
public interface IErasureLog
{
	#region Methods
	/// <summary>
	/// Stages the record of an erased subject; when the subject was erased before, the day of that record is moved to the new one.
	/// </summary>
	/// <param name="subjectHash">The keyed hash of the erased subject.</param>
	/// <param name="erasedOn">The day of the erasure.</param>
	/// <param name="cancellationToken">Cancels the read of an earlier record.</param>
	/// <returns>A task that completes when the record is staged; it is applied when the unit of work saves.</returns>
	Task RecordAsync(SubjectHash subjectHash, DateOnly erasedOn, CancellationToken cancellationToken = default);
	#endregion
}
