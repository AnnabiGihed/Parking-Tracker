using Microsoft.EntityFrameworkCore;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The repository of the user accounts on PostgreSQL. Every read loads the sessions with the account, because the
///              account decides over them; the tokens are looked up through the unique index on the token hash. Changes are staged in
///              the context and applied by the unit of work. Deleting an account is a hard delete, and the sessions go with it.
/// </summary>
internal sealed class UserAccountRepository : BaseAsyncCommandRepository<UserAccount, UserAccountId>, IUserAccountRepository
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="UserAccountRepository"/> class.
	/// </summary>
	/// <param name="dbContext">The server database context.</param>
	public UserAccountRepository(DbContext dbContext)
		: base(dbContext)
	{
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public override Task<UserAccount?> FindByIdAsync(UserAccountId id, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(id);
		return Accounts().FirstOrDefaultAsync(account => account.Id == id, ct);
	}

	/// <inheritdoc />
	public Task<UserAccount?> FindBySubjectHashAsync(SubjectHash subjectHash, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(subjectHash);
		return Accounts().FirstOrDefaultAsync(account => account.SubjectHash == subjectHash, cancellationToken);
	}

	/// <inheritdoc />
	public Task<UserAccount?> FindByRefreshTokenHashAsync(RefreshTokenHash tokenHash, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(tokenHash);
		return Accounts().FirstOrDefaultAsync(account => account.Sessions.Any(session => session.TokenHash == tokenHash), cancellationToken);
	}

	/// <inheritdoc />
	public Task<UserAccount?> ReloadByRefreshTokenHashAsync(RefreshTokenHash tokenHash, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(tokenHash);
		DbContext.ChangeTracker.Clear();
		return FindByRefreshTokenHashAsync(tokenHash, cancellationToken);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Starts a query over the accounts that loads their sessions.
	/// </summary>
	/// <returns>The query.</returns>
	private IQueryable<UserAccount> Accounts() => DbContext.Set<UserAccount>().Include(account => account.Sessions);
	#endregion
}
