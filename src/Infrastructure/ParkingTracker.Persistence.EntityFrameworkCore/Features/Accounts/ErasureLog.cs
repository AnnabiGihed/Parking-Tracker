using Microsoft.EntityFrameworkCore;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The erasure log on PostgreSQL. A record is staged in the context and applied by the unit of work, in the same
///              transaction as the deletion of the account. A subject erased before (it signed in again and erased again) keeps its
///              one row, with the new day, because the subject hash is the key.
/// </summary>
internal sealed class ErasureLog : IErasureLog
{
	#region Fields
	/// <summary>
	/// The server database context.
	/// </summary>
	private readonly DbContext dbContext;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ErasureLog"/> class.
	/// </summary>
	/// <param name="dbContext">The server database context.</param>
	public ErasureLog(DbContext dbContext)
	{
		this.dbContext = dbContext;
	}
	#endregion

	#region IErasureLog Implementation
	/// <inheritdoc />
	public async Task RecordAsync(SubjectHash subjectHash, DateOnly erasedOn, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(subjectHash);
		var entries = dbContext.Set<ErasureLogEntry>();
		var existing = await entries.FindAsync([subjectHash.Value], cancellationToken).ConfigureAwait(false);
		if (existing is null)
		{
			_ = entries.Add(new ErasureLogEntry { SubjectHash = subjectHash.Value, ErasedOn = erasedOn });
			return;
		}

		existing.ErasedOn = erasedOn;
	}
	#endregion
}
