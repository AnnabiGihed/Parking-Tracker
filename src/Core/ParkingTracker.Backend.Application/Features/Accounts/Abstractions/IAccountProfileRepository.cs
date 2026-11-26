using ParkingTracker.Domain.Features.Accounts.Aggregates;
using Pivot.Framework.Domain.Repositories;

namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the encrypted profiles of the accounts (ADR-0032). A profile has the identifier of its account, so
///              <c>FindByIdAsync</c> with the account identifier finds it. Changes are staged and applied when the unit of work saves;
///              a save that replaces a profile that another save replaced meanwhile fails with a concurrency error, because the
///              revision is the concurrency token. Removing a profile is a hard delete.
/// </summary>
public interface IAccountProfileRepository : IAsyncCommandRepository<AccountProfile, UserAccountId>
{
}
