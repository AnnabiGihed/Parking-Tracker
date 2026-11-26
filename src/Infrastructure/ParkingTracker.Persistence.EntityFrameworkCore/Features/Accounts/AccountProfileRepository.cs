using Microsoft.EntityFrameworkCore;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using Pivot.Framework.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The repository of the encrypted account profiles on PostgreSQL. A profile is read by the identifier of its account.
///              Changes are staged in the context and applied by the unit of work; the revision is the concurrency token, so a
///              profile that was replaced since it was read fails the save. Deleting is a hard delete.
/// </summary>
internal sealed class AccountProfileRepository : BaseAsyncCommandRepository<AccountProfile, UserAccountId>, IAccountProfileRepository
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccountProfileRepository"/> class.
	/// </summary>
	/// <param name="dbContext">The server database context.</param>
	public AccountProfileRepository(DbContext dbContext)
		: base(dbContext)
	{
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public override Task<AccountProfile?> FindByIdAsync(UserAccountId id, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(id);
		return DbContext.Set<AccountProfile>().FirstOrDefaultAsync(profile => profile.Id == id, ct);
	}
	#endregion
}
