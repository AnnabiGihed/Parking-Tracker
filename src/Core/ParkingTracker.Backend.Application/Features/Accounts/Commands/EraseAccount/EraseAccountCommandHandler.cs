using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using Pivot.Framework.Application.Abstractions.Messaging.Commands;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.EraseAccount;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="EraseAccountCommand"/> (ADR-0015, architecture 6.1): deletes the account with its sessions and
///              records its subject hash in the erasure log, staged together and saved by one unit of work, so either both happen or
///              neither does. It is idempotent: an account that is gone is a success, also when a second request erased it first. The erasure log is not a ban; the same Google
///              account can sign in again and gets a new account.
/// </summary>
public sealed class EraseAccountCommandHandler : ICommandHandler<EraseAccountCommand>
{
	#region Fields
	/// <summary>
	/// The accounts.
	/// </summary>
	private readonly IUserAccountRepository accounts;

	/// <summary>
	/// The erasure log.
	/// </summary>
	private readonly IErasureLog erasureLog;

	/// <summary>
	/// Saves the deletion and the record in one transaction.
	/// </summary>
	private readonly IUnitOfWork unitOfWork;

	/// <summary>
	/// The clock.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="EraseAccountCommandHandler"/> class.
	/// </summary>
	/// <param name="accounts">The accounts.</param>
	/// <param name="erasureLog">The erasure log.</param>
	/// <param name="unitOfWork">Saves the deletion and the record in one transaction.</param>
	/// <param name="clock">The clock.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public EraseAccountCommandHandler(IUserAccountRepository accounts, IErasureLog erasureLog, IUnitOfWork unitOfWork, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(erasureLog);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(clock);
		this.accounts = accounts;
		this.erasureLog = erasureLog;
		this.unitOfWork = unitOfWork;
		this.clock = clock;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result> Handle(EraseAccountCommand request, CancellationToken cancellationToken)
	{
		var account = await accounts.FindByIdAsync(new UserAccountId(request.AccountId), cancellationToken).ConfigureAwait(false);
		if (account is null)
		{
			return Result.Success();
		}

		var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
		await erasureLog.RecordAsync(account.SubjectHash, today, cancellationToken).ConfigureAwait(false);
		await accounts.DeleteAsync(account, cancellationToken).ConfigureAwait(false);
		var save = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return save.IsSuccess || await IsGoneAsync(request.AccountId, cancellationToken).ConfigureAwait(false) ? Result.Success() : save;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Tells whether the account no longer exists. Two requests that erase the same account at once both read it; the one that
	/// loses the save finds it gone, and its goal, no account, is reached.
	/// </summary>
	/// <param name="accountId">The identifier of the account.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns><see langword="true"/> when the database has no such account.</returns>
	private async Task<bool> IsGoneAsync(Guid accountId, CancellationToken cancellationToken)
	{
		return await accounts.FindByIdAsync(new UserAccountId(accountId), cancellationToken).ConfigureAwait(false) is null;
	}
	#endregion
}
