using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the phone's own unit of work (ADR-0026): there are no domain events to dispatch and no outbox, so it only
///              commits or rolls back and says which in a <see cref="Result"/>. Every operation that changes the database begins its
///              own <see cref="IDeviceChangeScope"/>; the unit of work lets one scope at a time stage and commit, so that the store, the
///              migration and any background writer cannot interleave their changes or apply each other's.
/// </summary>
public interface IDeviceUnitOfWork
{
	#region Methods
	/// <summary>
	/// Begins a scope that collects the changes of one operation. It waits until the previous scope was disposed.
	/// </summary>
	/// <param name="cancellationToken">Cancels the wait for the previous scope.</param>
	/// <returns>A task with the scope; dispose it when the operation is done, also when it failed.</returns>
	Task<IDeviceChangeScope> BeginAsync(CancellationToken cancellationToken = default);
	#endregion
}
