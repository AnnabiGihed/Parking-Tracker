using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.Persistence.Device.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The device unit of work (ADR-0026). It hands out one <see cref="DeviceChangeScope"/> at a time: a scope holds the one
///              write gate of the database from <see cref="BeginAsync"/> until it is disposed, so that the settings store, the move of
///              the old settings and any background writer never stage or commit at the same time, and one caller can never commit
///              the changes another one staged. Each scope owns its staged changes.
/// </summary>
public sealed class DeviceUnitOfWork : IDeviceUnitOfWork, IDisposable
{
	#region Fields
	/// <summary>
	/// The database that receives the changes.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<DeviceUnitOfWork> logger;

	/// <summary>
	/// The one write gate of the database; held by the open scope.
	/// </summary>
	private readonly SemaphoreSlim writeGate = new(1, 1);
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceUnitOfWork"/> class.
	/// </summary>
	/// <param name="database">The database that receives the changes.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public DeviceUnitOfWork(DeviceDatabase database, ILogger<DeviceUnitOfWork> logger)
	{
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(logger);
		this.database = database;
		this.logger = logger;
	}
	#endregion

	#region IDeviceUnitOfWork Implementation
	/// <inheritdoc />
	public async Task<IDeviceChangeScope> BeginAsync(CancellationToken cancellationToken = default)
	{
		await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		return new DeviceChangeScope(database, writeGate, logger);
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		writeGate.Dispose();
	}
	#endregion
}
