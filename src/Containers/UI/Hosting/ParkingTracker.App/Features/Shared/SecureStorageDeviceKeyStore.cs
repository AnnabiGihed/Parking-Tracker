using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using ParkingTracker.Persistence.Device.Features.Shared.Encryption;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The key of the field cipher of the device database (ADR-0026), kept in MAUI <c>SecureStorage</c> under
///              <c>pt.dbKey</c>: a 256-bit key from the random generator of the system, stored as base64. <c>SecureStorage</c> wraps
///              its values with a key in the Android keystore, and its preferences file is excluded from backup, so the key stays on
///              this phone; a restore on a new phone finds no key and the fields cannot be read. Reading never creates a key: the
///              answer "no key" is the failure <see cref="DeviceDatabaseErrors.KeyMissing"/>, and the field cipher creates one with
///              <see cref="CreateCurrentAsync"/> only after it checked that no encrypted row exists (<c>SecureStorage</c> answers
///              <see langword="null"/> after it failed to decrypt and removed the entry, and a new key behind existing rows would orphan
///              them). When the storage throws, or holds a value that is not a key, the answer is the failure
///              <see cref="DeviceDataErrors.KeyUnavailable"/> and nothing is written: replacing a key that could not be read would make
///              every stored field unreadable for good. Only one key exists, with the identifier <c>1</c>; a later release can add
///              another and keep reading the old values.
/// </summary>
public sealed partial class SecureStorageDeviceKeyStore : IDeviceKeyStore, IDisposable
{
	#region Constants
	/// <summary>
	/// The key under which <c>SecureStorage</c> keeps the key.
	/// </summary>
	private const string StorageKey = "pt.dbKey";

	/// <summary>
	/// The identifier of the only key.
	/// </summary>
	private const string CurrentKeyId = "1";

	/// <summary>
	/// The length of the key in bytes.
	/// </summary>
	private const int KeyLength = 32;
	#endregion

	#region Fields
	/// <summary>
	/// Lets one caller create the key; a second caller would otherwise create another and orphan the first one's values.
	/// </summary>
	private readonly SemaphoreSlim gate = new(1, 1);

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<SecureStorageDeviceKeyStore> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SecureStorageDeviceKeyStore"/> class.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
	public SecureStorageDeviceKeyStore(ILogger<SecureStorageDeviceKeyStore> logger)
	{
		ArgumentNullException.ThrowIfNull(logger);
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a storage that threw when the key was read; the key is not replaced.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the storage reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The protected storage failed to give the key of the device database; the key is kept and tried again later")]
	private static partial void LogReadFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs a storage that threw when the new key was saved; the key is not used.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the storage reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The protected storage failed to save the new key of the device database; no data is encrypted with it")]
	private static partial void LogWriteFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs a stored value that is not a 256-bit key; it is not replaced.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The stored key of the device database is not a 256-bit key; it is kept and not replaced")]
	private static partial void LogStoredKeyInvalid(ILogger logger);
	#endregion

	#region IDeviceKeyStore Implementation
	/// <inheritdoc />
	public async Task<Result<DeviceKey>> GetCurrentAsync(CancellationToken cancellationToken = default)
	{
		return await FindAsync(CurrentKeyId, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<Result<DeviceKey>> CreateCurrentAsync(CancellationToken cancellationToken = default)
	{
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var (readable, saved) = await ReadSavedAsync().ConfigureAwait(false);
			if (!readable)
			{
				return Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable);
			}

			// A key that appeared since the caller looked is the key; only a storage that answered "nothing saved" may get a new one.
			return saved is null ? await CreateAsync().ConfigureAwait(false) : ParseSaved(saved);
		}
		finally
		{
			_ = gate.Release();
		}
	}

	/// <inheritdoc />
	public async Task<Result<DeviceKey>> FindAsync(string keyId, CancellationToken cancellationToken = default)
	{
		if (!string.Equals(keyId, CurrentKeyId, StringComparison.Ordinal))
		{
			return Result.Failure<DeviceKey>(DeviceDatabaseErrors.KeyMissing, ResultExceptionType.NotFound);
		}

		var (readable, saved) = await ReadSavedAsync().ConfigureAwait(false);
		if (!readable)
		{
			return Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable);
		}

		return saved is null ? Result.Failure<DeviceKey>(DeviceDatabaseErrors.KeyMissing, ResultExceptionType.NotFound) : ParseSaved(saved);
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		gate.Dispose();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the saved key text. <c>SecureStorage</c> itself answers <see langword="null"/> when the keystore key is gone (after a
	/// lock-screen change or a restore); that is the one case that counts as "no key". An exception is not an answer.
	/// </summary>
	/// <returns>A task with whether the storage answered, and the base64 text or <see langword="null"/> when it holds none.</returns>
	private async Task<(bool Readable, string? Saved)> ReadSavedAsync()
	{
		try
		{
			return (true, await SecureStorage.Default.GetAsync(StorageKey).ConfigureAwait(false));
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogReadFailed(logger, exception);
			return (false, null);
		}
	}

	/// <summary>
	/// Creates a key and saves it; the key is only returned when the storage accepted it.
	/// </summary>
	/// <returns>A task with the new key, or the failure when the storage could not save it.</returns>
	private async Task<Result<DeviceKey>> CreateAsync()
	{
		var material = RandomNumberGenerator.GetBytes(KeyLength);
		try
		{
			await SecureStorage.Default.SetAsync(StorageKey, Convert.ToBase64String(material)).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogWriteFailed(logger, exception);
			return Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable);
		}

		return new DeviceKey(CurrentKeyId, material);
	}

	/// <summary>
	/// Reads a saved key.
	/// </summary>
	/// <param name="saved">The base64 text in the storage.</param>
	/// <returns>The key, or the failure <see cref="DeviceDataErrors.KeyUnavailable"/> when the text is not a 256-bit key; it is kept as it is.</returns>
	private Result<DeviceKey> ParseSaved(string saved)
	{
		try
		{
			var material = Convert.FromBase64String(saved);
			if (material.Length == KeyLength)
			{
				return new DeviceKey(CurrentKeyId, material);
			}
		}
		catch (FormatException)
		{
			// Handled like a wrong length below: the text is not a key.
		}

		LogStoredKeyInvalid(logger);
		return Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable);
	}
	#endregion
}
