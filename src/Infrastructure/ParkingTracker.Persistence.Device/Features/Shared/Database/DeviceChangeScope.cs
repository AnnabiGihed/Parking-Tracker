using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Persistence.Device.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The changes of one operation on the device database (ADR-0026): the repositories stage their inserts, updates and
///              deletes here, and <see cref="CommitAsync"/> applies them in one <c>RunInTransactionAsync</c> call. sqlite-net commits
///              when the delegate returns and rolls back when it throws; the exception becomes a failed <see cref="Result"/> and the
///              staged changes are dropped. The token is checked before the transaction starts, because the call itself takes none.
///              The scope owns the write gate of <see cref="DeviceUnitOfWork"/> until it is disposed.
/// </summary>
internal sealed partial class DeviceChangeScope : IDeviceChangeScope
{
	#region Fields
	/// <summary>
	/// The database that receives the changes.
	/// </summary>
	private readonly DeviceDatabase database;

	/// <summary>
	/// The write gate that this scope holds until it is disposed.
	/// </summary>
	private readonly SemaphoreSlim writeGate;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger logger;

	/// <summary>
	/// The changes that wait for the next commit, in the order they were staged; only this scope's operation touches them.
	/// </summary>
	private readonly List<Action<SQLiteConnection>> staged = [];

	/// <summary>
	/// Whether the scope was disposed and gave the write gate back (0 or 1).
	/// </summary>
	private int disposed;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceChangeScope"/> class for a caller that already holds the write gate.
	/// </summary>
	/// <param name="database">The database that receives the changes.</param>
	/// <param name="writeGate">The write gate that the caller took; the scope releases it when it is disposed.</param>
	/// <param name="logger">The log.</param>
	internal DeviceChangeScope(DeviceDatabase database, SemaphoreSlim writeGate, ILogger logger)
	{
		this.database = database;
		this.writeGate = writeGate;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a commit that was rolled back.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the database reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The device database rolled back a commit")]
	private static partial void LogCommitFailed(ILogger logger, Exception exception);
	#endregion

	#region IDeviceChangeScope Implementation
	/// <inheritdoc />
	public async Task<Result> CommitAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
		var changes = staged.ToArray();
		staged.Clear();
		cancellationToken.ThrowIfCancellationRequested();
		if (changes.Length == 0)
		{
			return Result.Success();
		}

		try
		{
			await database.Connection.RunInTransactionAsync(connection =>
			{
				foreach (var change in changes)
				{
					change(connection);
				}
			}).ConfigureAwait(false);
			return Result.Success();
		}
		catch (Exception exception) when (exception is SQLiteException or InvalidOperationException or IOException)
		{
			LogCommitFailed(logger, exception);
			return Result.Failure(DeviceDatabaseErrors.CommitFailed);
		}
	}
	#endregion

	#region IAsyncDisposable Implementation
	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref disposed, 1) == 0)
		{
			staged.Clear();
			_ = writeGate.Release();
		}

		return ValueTask.CompletedTask;
	}
	#endregion

	#region Internal Methods
	/// <summary>
	/// Gets the scope behind the port; the repositories of this project only work with scopes of their own unit of work.
	/// </summary>
	/// <param name="scope">The scope the caller got from <see cref="IDeviceUnitOfWork.BeginAsync"/>.</param>
	/// <returns>The same scope as its implementation type.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="scope"/> does not come from <see cref="DeviceUnitOfWork"/>.</exception>
	internal static DeviceChangeScope From(IDeviceChangeScope scope)
	{
		ArgumentNullException.ThrowIfNull(scope);
		return scope as DeviceChangeScope ?? throw new ArgumentException("The scope does not come from the device unit of work.", nameof(scope));
	}

	/// <summary>
	/// Stages a change for the next commit; the change runs inside the transaction, on the connection that the transaction uses.
	/// </summary>
	/// <param name="change">The change.</param>
	/// <exception cref="ObjectDisposedException">The scope was disposed.</exception>
	internal void Stage(Action<SQLiteConnection> change)
	{
		ArgumentNullException.ThrowIfNull(change);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
		staged.Add(change);
	}
	#endregion
}
