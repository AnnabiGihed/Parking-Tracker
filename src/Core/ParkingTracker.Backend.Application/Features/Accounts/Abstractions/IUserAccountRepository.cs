using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Repositories;

namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the user accounts of the server. Every account is loaded with its sessions. Changes are staged and
///              applied when the unit of work saves; removing an account is a hard delete that takes its sessions with it.
/// </summary>
public interface IUserAccountRepository : IAsyncCommandRepository<UserAccount, UserAccountId>
{
	#region Methods
	/// <summary>
	/// Finds the account of a Google subject.
	/// </summary>
	/// <param name="subjectHash">The keyed hash of the subject.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the account and its sessions, or <see langword="null"/> when there is none.</returns>
	Task<UserAccount?> FindBySubjectHashAsync(SubjectHash subjectHash, CancellationToken cancellationToken = default);

	/// <summary>
	/// Finds the account that holds a refresh token, through the unique index on the token hash.
	/// </summary>
	/// <param name="tokenHash">The hash of the refresh token.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the account and its sessions, or <see langword="null"/> when no session has the token.</returns>
	Task<UserAccount?> FindByRefreshTokenHashAsync(RefreshTokenHash tokenHash, CancellationToken cancellationToken = default);
	#endregion
}
