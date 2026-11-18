using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that brings the schema of the device database up to date. The start-up gate calls it once per launch, before
///              anything reads a repository (ADR-0026).
/// </summary>
public interface IDeviceDatabaseInitializer
{
	#region Methods
	/// <summary>
	/// Applies the schema steps that the database has not seen yet, in order.
	/// </summary>
	/// <param name="cancellationToken">Cancels the work between steps.</param>
	/// <returns>A task with success, or a failure when a step failed or the database was written by a newer version of the app.</returns>
	Task<Result> InitializeAsync(CancellationToken cancellationToken = default);
	#endregion
}
