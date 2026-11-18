using System.Security.Cryptography;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Testing.Device;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A key store that keeps one key in memory, for the tests of the cipher, the repositories and the settings store; the key
///              material and the identifier can be chosen, so that a test can open the same database with another key. It can start
///              without a key, or lose the key it holds, as the protected storage of the phone does after it dropped an entry it failed
///              to decrypt; reading then answers "no key", and only <see cref="CreateCurrentAsync"/> makes a new one.
/// </summary>
public sealed class InMemoryKeyStore : IDeviceKeyStore
{
	#region Fields
	/// <summary>
	/// The one key.
	/// </summary>
	private DeviceKey? key;

	/// <summary>
	/// The identifier of a key that is created later.
	/// </summary>
	private readonly string newKeyId = "k2";
	#endregion

	#region Properties
	/// <summary>
	/// Gets or sets a value indicating whether the storage answers that the key cannot be read now.
	/// </summary>
	public bool IsUnavailable { get; set; }

	/// <summary>
	/// Gets how many keys were created by <see cref="CreateCurrentAsync"/>.
	/// </summary>
	public int CreatedCount { get; private set; }
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="InMemoryKeyStore"/> class with a random 256-bit key.
	/// </summary>
	/// <param name="id">The identifier of the key.</param>
	public InMemoryKeyStore(string id = "k1")
		: this(new DeviceKey(id, RandomNumberGenerator.GetBytes(32)))
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="InMemoryKeyStore"/> class with the given key.
	/// </summary>
	/// <param name="key">The key the store holds.</param>
	public InMemoryKeyStore(DeviceKey key)
	{
		this.key = key;
	}
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates a key store that holds no key yet, as a new phone does.
	/// </summary>
	/// <returns>The key store.</returns>
	public static InMemoryKeyStore WithoutKey() => new InMemoryKeyStore().Lose();
	#endregion

	#region Public Methods
	/// <summary>
	/// Drops the key, as the protected storage does after it failed to decrypt the entry.
	/// </summary>
	/// <returns>This key store.</returns>
	public InMemoryKeyStore Lose()
	{
		key = null;
		return this;
	}
	#endregion

	#region IDeviceKeyStore Implementation
	/// <inheritdoc />
	public Task<Result<DeviceKey>> GetCurrentAsync(CancellationToken cancellationToken = default)
	{
		if (IsUnavailable)
		{
			return Task.FromResult(Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable));
		}

		return Task.FromResult(key is { } held
			? Result.Success(held)
			: Result.Failure<DeviceKey>(DeviceDatabaseErrors.KeyMissing, ResultExceptionType.NotFound));
	}

	/// <inheritdoc />
	public Task<Result<DeviceKey>> CreateCurrentAsync(CancellationToken cancellationToken = default)
	{
		if (IsUnavailable)
		{
			return Task.FromResult(Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable));
		}

		if (key is null)
		{
			key = new DeviceKey(newKeyId, RandomNumberGenerator.GetBytes(32));
			CreatedCount++;
		}

		return Task.FromResult(Result.Success(key));
	}

	/// <inheritdoc />
	public Task<Result<DeviceKey>> FindAsync(string keyId, CancellationToken cancellationToken = default)
	{
		if (IsUnavailable)
		{
			return Task.FromResult(Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable));
		}

		return Task.FromResult(key is { } held && string.Equals(keyId, held.Id, StringComparison.Ordinal)
			? Result.Success(held)
			: Result.Failure<DeviceKey>(DeviceDatabaseErrors.KeyMissing, ResultExceptionType.NotFound));
	}
	#endregion
}
