using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Queries.GetMe;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="GetMeQuery"/>: reads the account with its sessions and counts the devices still signed in. An
///              account that no longer exists (erased) is an authentication failure and not a not-found: its old access token must
///              stop working, so the caller answers 401.
/// </summary>
public sealed class GetMeQueryHandler : IQueryHandler<GetMeQuery, GetMeResponse>
{
	#region Fields
	/// <summary>
	/// The accounts.
	/// </summary>
	private readonly IUserAccountRepository accounts;

	/// <summary>
	/// The clock.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GetMeQueryHandler"/> class.
	/// </summary>
	/// <param name="accounts">The accounts.</param>
	/// <param name="clock">The clock.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public GetMeQueryHandler(IUserAccountRepository accounts, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(clock);
		this.accounts = accounts;
		this.clock = clock;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<GetMeResponse>> Handle(GetMeQuery request, CancellationToken cancellationToken)
	{
		if (request.AccountId == Guid.Empty)
		{
			return Result.Failure<GetMeResponse>(SessionErrors.AccountUnknown, ResultExceptionType.AuthenticationRequired);
		}

		var account = await accounts.FindByIdAsync(new UserAccountId(request.AccountId), cancellationToken).ConfigureAwait(false);
		return account is null
			? Result.Failure<GetMeResponse>(SessionErrors.AccountUnknown, ResultExceptionType.AuthenticationRequired)
			: new GetMeResponse(account.Id.Value, account.CreatedOn, account.LastSeenOn, account.ActiveSessionCount(clock.GetUtcNow()));
	}
	#endregion
}
