using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Persistence.Device.Features.ParkingSessions;
using ParkingTracker.Persistence.Device.Features.ResidentCards;
using ParkingTracker.Persistence.Device.Features.SavedPlaces;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using Pivot.Framework.Domain.Shared;
using SQLite;

namespace ParkingTracker.Testing.Device;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A real device database for one test, shared by the tests of the database and of the settings store: a SQLite file in
///              its own temporary folder, brought up to date by the real initializer, with the real unit of work, cipher, probe and
///              repositories over a fixed set of municipalities. Running against the real file proves that the native SQLite library of
///              SQLitePCLRaw loads on the test host and that the SQL is valid. Raw SQL goes through a second connection to the file, so
///              the helper needs nothing but the public API of the database.
/// </summary>
public sealed class DeviceTestDatabase : IAsyncDisposable
{
	#region Fields
	/// <summary>
	/// The temporary folder that holds the database file.
	/// </summary>
	private readonly string folder = Path.Combine(Path.GetTempPath(), "pt-device-db-" + Guid.NewGuid().ToString("N"));

	/// <summary>
	/// The connection for raw SQL, opened on first use.
	/// </summary>
	private readonly Lazy<SQLiteAsyncConnection> raw;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceTestDatabase"/> class; the schema is not created yet, see <see cref="CreateAsync"/>.
	/// </summary>
	/// <param name="keys">The key store of the cipher; a random key when <see langword="null"/>.</param>
	private DeviceTestDatabase(IDeviceKeyStore? keys)
	{
		_ = Directory.CreateDirectory(folder);
		FilePath = Path.Combine(folder, "parkingtracker.db");
		Database = new DeviceDatabase(FilePath);
		raw = new Lazy<SQLiteAsyncConnection>(() => new SQLiteAsyncConnection(FilePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex));
		Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
		Initializer = new DeviceDatabaseInitializer(Database, Clock, NullLogger<DeviceDatabaseInitializer>.Instance);
		UnitOfWork = new DeviceUnitOfWork(Database, NullLogger<DeviceUnitOfWork>.Instance);
		Keys = keys ?? new InMemoryKeyStore();
		Probe = new EncryptedDataProbe(Database, NullLogger<EncryptedDataProbe>.Instance);
		Cipher = new FieldCipher(Keys, Probe, NullLogger<FieldCipher>.Instance);
		ReferenceData = Substitute.For<IReferenceData>();
		ReferenceData.GetReferenceData().Returns(Result.Success(Data()));
		Cards = new ResidentCardRepository(Database, Cipher, ReferenceData, NullLogger<ResidentCardRepository>.Instance);
		Places = new SavedPlacesRepository(Database, Cipher, NullLogger<SavedPlacesRepository>.Instance);
		Sessions = new ParkingSessionRepository(Database, Cipher, NullLogger<ParkingSessionRepository>.Instance);
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the path of the database file.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the database.
	/// </summary>
	public DeviceDatabase Database { get; }

	/// <summary>
	/// Gets the fake clock of the initializer.
	/// </summary>
	public FakeTimeProvider Clock { get; }

	/// <summary>
	/// Gets the schema initializer.
	/// </summary>
	public DeviceDatabaseInitializer Initializer { get; }

	/// <summary>
	/// Gets the unit of work.
	/// </summary>
	public DeviceUnitOfWork UnitOfWork { get; }

	/// <summary>
	/// Gets the key store of the cipher.
	/// </summary>
	public IDeviceKeyStore Keys { get; }

	/// <summary>
	/// Gets the probe that tells whether encrypted rows exist.
	/// </summary>
	public EncryptedDataProbe Probe { get; }

	/// <summary>
	/// Gets the key store as the in-memory one that a test can switch off or make lose its key.
	/// </summary>
	/// <exception cref="InvalidCastException">The test passed another key store.</exception>
	public InMemoryKeyStore MemoryKeys => (InMemoryKeyStore)Keys;

	/// <summary>
	/// Gets the cipher.
	/// </summary>
	public FieldCipher Cipher { get; }

	/// <summary>
	/// Gets the reference data substitute.
	/// </summary>
	public IReferenceData ReferenceData { get; }

	/// <summary>
	/// Gets the repository of the residents' card.
	/// </summary>
	public ResidentCardRepository Cards { get; }

	/// <summary>
	/// Gets the repository of the saved places.
	/// </summary>
	public SavedPlacesRepository Places { get; }

	/// <summary>
	/// Gets the repository of the parking session.
	/// </summary>
	public ParkingSessionRepository Sessions { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates a database in a new temporary folder and brings its schema up to date.
	/// </summary>
	/// <param name="keys">The key store of the cipher; a random key when <see langword="null"/>.</param>
	/// <returns>A task with the database.</returns>
	public static async Task<DeviceTestDatabase> CreateAsync(IDeviceKeyStore? keys = null)
	{
		var created = new DeviceTestDatabase(keys);
		var ready = await created.Initializer.InitializeAsync().ConfigureAwait(false);
		if (ready.IsFailure)
		{
			throw new InvalidOperationException("The test database could not be created: " + ready.Error.Message);
		}

		return created;
	}

	/// <summary>
	/// Creates a database in a new temporary folder without creating any table.
	/// </summary>
	/// <returns>The empty database file holder.</returns>
	public static DeviceTestDatabase CreateEmpty() => new(null);
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads one value with raw SQL, bypassing the repositories, to look at what is really stored.
	/// </summary>
	/// <typeparam name="T">The type of the value.</typeparam>
	/// <param name="sql">The SQL that returns one value.</param>
	/// <returns>A task with the value.</returns>
	public Task<T> ScalarAsync<T>(string sql) => raw.Value.ExecuteScalarAsync<T>(sql);

	/// <summary>
	/// Encrypts a text the way a repository does for a column, so that a test can put a value into a row with raw SQL.
	/// </summary>
	/// <param name="table">The table of the row.</param>
	/// <param name="column">The column of the value.</param>
	/// <param name="rowId">The identifier of the row.</param>
	/// <param name="plaintext">The text to encrypt.</param>
	/// <returns>A task with the text to store.</returns>
	public async Task<string> SealAsync(string table, string column, string rowId, string plaintext)
	{
		return (await Cipher.EncryptAsync(plaintext, new FieldBinding(table, column, rowId)).ConfigureAwait(false)).Value;
	}

	/// <summary>
	/// Begins a scope, lets the test stage changes in it and commits them, the way an operation of the app does.
	/// </summary>
	/// <param name="stage">Stages the changes in the scope and tells whether that worked.</param>
	/// <returns>A task with the failure of the staging, or the result of the commit.</returns>
	public async Task<Result> ApplyAsync(Func<IDeviceChangeScope, Task<Result>> stage)
	{
		var scope = await UnitOfWork.BeginAsync().ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var staged = await stage(scope).ConfigureAwait(false);
			return staged.IsFailure ? staged : await scope.CommitAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Begins a scope, lets the test stage a removal in it and commits it.
	/// </summary>
	/// <param name="remove">Stages the removal in the scope.</param>
	/// <returns>A task with the result of the commit.</returns>
	public async Task<Result> ApplyRemovalAsync(Action<IDeviceChangeScope> remove)
	{
		var scope = await UnitOfWork.BeginAsync().ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			remove(scope);
			return await scope.CommitAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Runs raw SQL, bypassing the repositories, to put the database in a state the aggregates would not write.
	/// </summary>
	/// <param name="sql">The SQL to run.</param>
	/// <returns>A task that completes when the SQL has run.</returns>
	public async Task ExecuteAsync(string sql) => _ = await raw.Value.ExecuteAsync(sql).ConfigureAwait(false);
	#endregion

	#region IAsyncDisposable Implementation
	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (raw.IsValueCreated)
		{
			await raw.Value.CloseAsync().ConfigureAwait(false);
		}

		await Database.DisposeAsync().ConfigureAwait(false);
		Initializer.Dispose();
		UnitOfWork.Dispose();
		try
		{
			Directory.Delete(folder, recursive: true);
		}
		catch (IOException)
		{
			// A temporary folder that the system still holds is cleaned by the system later; the test result does not depend on it.
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the reference data: the City of Brussels (street lists) and Ixelles (areas with the prefix IXLR and the border zone LIM_X).
	/// </summary>
	/// <returns>The reference data.</returns>
	private static ReferenceDataSet Data() => new([TestData.City, TestData.Ixelles], [], []);
	#endregion
}
