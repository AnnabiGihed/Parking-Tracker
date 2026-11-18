using SQLite;

namespace ParkingTracker.Persistence.Device.Features.Shared.Database;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The one connection of the process to the device database file (ADR-0026), opened with read-write, create and
///              full-mutex flags; sqlite-net serialises the calls on it. The connection opens its file on first use, so creating
///              this object costs nothing at start-up.
/// </summary>
public sealed class DeviceDatabase : IAsyncDisposable
{
	#region Fields
	/// <summary>
	/// The lazily opened connection.
	/// </summary>
	private readonly Lazy<SQLiteAsyncConnection> connection;
	#endregion

	#region Properties
	/// <summary>
	/// Gets the full path of the database file.
	/// </summary>
	public string Path { get; }

	/// <summary>
	/// Gets the connection; the first call opens the file.
	/// </summary>
	internal SQLiteAsyncConnection Connection => connection.Value;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceDatabase"/> class.
	/// </summary>
	/// <param name="path">The full path of the database file, in the private data directory of the app.</param>
	/// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or blank.</exception>
	public DeviceDatabase(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		Path = path;
		connection = new Lazy<SQLiteAsyncConnection>(
			() => new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex),
			LazyThreadSafetyMode.ExecutionAndPublication);
	}
	#endregion

	#region IAsyncDisposable Implementation
	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (connection.IsValueCreated)
		{
			await connection.Value.CloseAsync().ConfigureAwait(false);
		}
	}
	#endregion
}
