using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The changes of one operation on the device database (ADR-0026). The repositories stage their inserts, updates and
///              deletes in the scope they are given, and <see cref="CommitAsync"/> applies them in one database transaction: all of
///              them are saved or none is. The scope belongs to one operation, so changes staged by one caller are never committed by
///              another. Disposing the scope drops what was not committed and lets the next scope begin.
/// </summary>
public interface IDeviceChangeScope : IAsyncDisposable
{
	#region Methods
	/// <summary>
	/// Applies the staged changes together: all of them are saved or none is. The scope can stage more changes afterwards.
	/// </summary>
	/// <param name="cancellationToken">Checked before the transaction starts; a transaction that has started cannot be cancelled.</param>
	/// <returns>A task with success, or a failure when the database refused a change; the staged changes are dropped either way.</returns>
	Task<Result> CommitAsync(CancellationToken cancellationToken = default);
	#endregion
}
