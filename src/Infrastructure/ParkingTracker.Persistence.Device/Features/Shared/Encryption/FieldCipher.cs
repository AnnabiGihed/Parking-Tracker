using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Persistence.Device.Features.Shared.Database;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Persistence.Device.Features.Shared.Encryption;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Encrypts single fields of the device database (the card number, the plate and the coordinates) with AES-GCM
///              (ADR-0026). Each value gets a fresh random 96-bit nonce, and is written as <c>keyId.base64(nonce, tag, ciphertext)</c>
///              so that the key can be replaced later. The key identifier and the <see cref="FieldBinding"/> (table, column, row) are
///              the associated data of the encryption, so a value that was changed, moved to another column or row, labelled with
///              another key, or written with another key, never decrypts: the tag check fails and the caller gets a failed
///              <see cref="Result"/> instead of wrong data. A key that cannot be read now is a different failure
///              (<see cref="DeviceDataErrors.KeyUnavailable"/>) from a value that does not authenticate.
/// </summary>
public sealed partial class FieldCipher
{
	#region Constants
	/// <summary>
	/// The length of the key in bytes (256 bits).
	/// </summary>
	private const int KeyLength = 32;

	/// <summary>
	/// The length of the nonce in bytes (96 bits, the size GCM is built for).
	/// </summary>
	private const int NonceLength = 12;

	/// <summary>
	/// The length of the authentication tag in bytes (128 bits).
	/// </summary>
	private const int TagLength = 16;

	/// <summary>
	/// What separates the key identifier from the encrypted bytes.
	/// </summary>
	private const char Separator = '.';

	/// <summary>
	/// What separates the parts of the associated data; a control character that no name or identifier contains.
	/// </summary>
	private const char DataSeparator = '\u001f';
	#endregion

	#region Fields
	/// <summary>
	/// Where the key comes from.
	/// </summary>
	private readonly IDeviceKeyStore keys;

	/// <summary>
	/// Tells whether encrypted rows exist, before a key is created.
	/// </summary>
	private readonly IEncryptedDataProbe probe;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<FieldCipher> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FieldCipher"/> class.
	/// </summary>
	/// <param name="keys">Where the key comes from.</param>
	/// <param name="probe">Tells whether encrypted rows exist, before a key is created.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public FieldCipher(IDeviceKeyStore keys, IEncryptedDataProbe probe, ILogger<FieldCipher> logger)
	{
		ArgumentNullException.ThrowIfNull(keys);
		ArgumentNullException.ThrowIfNull(probe);
		ArgumentNullException.ThrowIfNull(logger);
		this.keys = keys;
		this.probe = probe;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a key store that threw instead of answering.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="exception">What the key store reported.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The key store of the device database failed; the key is treated as unavailable")]
	private static partial void LogKeyStoreFailed(ILogger logger, Exception exception);

	/// <summary>
	/// Logs a key that is not 256 bits long.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="length">The length of the key in bytes.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The key store gave a key of {Length} bytes instead of 32; the key is treated as unavailable")]
	private static partial void LogKeyLengthWrong(ILogger logger, int length);

	/// <summary>
	/// Logs a missing key while encrypted rows exist; no key is created.
	/// </summary>
	/// <param name="logger">The log.</param>
	[LoggerMessage(Level = LogLevel.Error, Message = "The key store holds no key, but the device database holds encrypted values; the key was lost and no new key is created")]
	private static partial void LogKeyLost(ILogger logger);
	#endregion

	#region Public Methods
	/// <summary>
	/// Encrypts a text with the current key.
	/// </summary>
	/// <param name="plaintext">The text to protect.</param>
	/// <param name="binding">Where the value will be stored; only a read with the same binding decrypts it.</param>
	/// <param name="cancellationToken">Cancels the request for the key.</param>
	/// <returns>A task with the text to store, or a failure when the key is not available.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="plaintext"/> is <see langword="null"/>.</exception>
	public async Task<Result<string>> EncryptAsync(string plaintext, FieldBinding binding, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plaintext);
		var key = await GetKeyAsync(() => CurrentOrNewAsync(cancellationToken)).ConfigureAwait(false);
		if (key.IsFailure)
		{
			return Result.Failure<string>(key.Error, key.ResultExceptionType);
		}

		var plain = Encoding.UTF8.GetBytes(plaintext);
		var nonce = RandomNumberGenerator.GetBytes(NonceLength);
		var cipher = new byte[plain.Length];
		var tag = new byte[TagLength];
		using (var aes = new AesGcm(key.Value.Material.Span, TagLength))
		{
			aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(key.Value.Id, binding));
		}

		var payload = new byte[NonceLength + TagLength + cipher.Length];
		nonce.CopyTo(payload, 0);
		tag.CopyTo(payload, NonceLength);
		cipher.CopyTo(payload, NonceLength + TagLength);
		return key.Value.Id + Separator + Convert.ToBase64String(payload);
	}

	/// <summary>
	/// Decrypts a stored value with the key it names.
	/// </summary>
	/// <param name="stored">The text that <see cref="EncryptAsync"/> returned.</param>
	/// <param name="binding">Where the value is stored; it must be the binding it was encrypted with.</param>
	/// <param name="cancellationToken">Cancels the request for the key.</param>
	/// <returns>A task with the text, or a failure when the format is unknown, the phone has no such key
	/// (<see cref="DeviceDatabaseErrors.KeyMissing"/>), the key cannot be read now (<see cref="DeviceDataErrors.KeyUnavailable"/>) or the value does not authenticate.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="stored"/> is <see langword="null"/>.</exception>
	public async Task<Result<string>> DecryptAsync(string stored, FieldBinding binding, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(stored);
		var separator = stored.IndexOf(Separator, StringComparison.Ordinal);
		if (separator <= 0 || !TryDecode(stored[(separator + 1)..], out var payload))
		{
			return Result.Failure<string>(DeviceDatabaseErrors.CiphertextMalformed);
		}

		var keyId = stored[..separator];
		var key = await GetKeyAsync(() => keys.FindAsync(keyId, cancellationToken)).ConfigureAwait(false);
		return key.IsFailure ? Result.Failure<string>(key.Error, key.ResultExceptionType) : Open(key.Value, payload, AssociatedData(keyId, binding));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Gets the current key. When the storage holds none, a key is created only if no encrypted row exists: with rows, a new key would
	/// leave them unreadable behind a silent replacement, so the answer is <see cref="DeviceDataErrors.KeyLost"/> instead.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>A task with the key, or the failure.</returns>
	private async Task<Result<DeviceKey>> CurrentOrNewAsync(CancellationToken cancellationToken)
	{
		var current = await keys.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
		if (current.IsSuccess || current.Error != DeviceDatabaseErrors.KeyMissing)
		{
			return current;
		}

		var anyRows = await probe.AnyAsync(cancellationToken).ConfigureAwait(false);
		if (anyRows.IsFailure)
		{
			return Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable);
		}

		if (anyRows.Value)
		{
			LogKeyLost(logger);
			return Result.Failure<DeviceKey>(DeviceDataErrors.KeyLost);
		}

		return await keys.CreateCurrentAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Asks the key store, turning an exception of the store or a key of the wrong length into the failure "key unavailable"; the
	/// cause is logged here, because the callers only see the error code.
	/// </summary>
	/// <param name="ask">The request to the key store.</param>
	/// <returns>A task with the key, or the failure.</returns>
	private async Task<Result<DeviceKey>> GetKeyAsync(Func<Task<Result<DeviceKey>>> ask)
	{
		try
		{
			var key = await ask().ConfigureAwait(false);
			if (key.IsSuccess && key.Value.Material.Length != KeyLength)
			{
				LogKeyLengthWrong(logger, key.Value.Material.Length);
				return Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable);
			}

			return key;
		}
		catch (Exception exception) when (exception is CryptographicException or InvalidOperationException or IOException or UnauthorizedAccessException)
		{
			// The platform's protected storage reports a lost or unreadable key with these; the data is not lost, so the caller retries later.
			LogKeyStoreFailed(logger, exception);
			return Result.Failure<DeviceKey>(DeviceDataErrors.KeyUnavailable);
		}
	}

	/// <summary>
	/// Builds the associated data that the value is authenticated with.
	/// </summary>
	/// <param name="keyId">The identifier of the key that encrypts or decrypts.</param>
	/// <param name="binding">Where the value is stored.</param>
	/// <returns>The bytes: key identifier, table, column and row, separated.</returns>
	private static byte[] AssociatedData(string keyId, FieldBinding binding)
	{
		return Encoding.UTF8.GetBytes(string.Join(DataSeparator, keyId, binding.Table, binding.Column, binding.RowId));
	}

	/// <summary>
	/// Reads the base64 payload and checks that it can hold a nonce and a tag.
	/// </summary>
	/// <param name="text">The part after the key identifier.</param>
	/// <param name="payload">The bytes when the text is valid.</param>
	/// <returns><see langword="true"/> when the payload has the shape the cipher writes.</returns>
	private static bool TryDecode(string text, out byte[] payload)
	{
		payload = [];
		try
		{
			payload = Convert.FromBase64String(text);
		}
		catch (FormatException)
		{
			return false;
		}

		return payload.Length >= NonceLength + TagLength;
	}

	/// <summary>
	/// Decrypts and authenticates a payload.
	/// </summary>
	/// <param name="key">The key the value names.</param>
	/// <param name="payload">Nonce, tag and ciphertext.</param>
	/// <param name="associatedData">What the value was bound to when it was encrypted.</param>
	/// <returns>The text, or a failure when the tag does not match; the caller logs the failure code.</returns>
	private static Result<string> Open(DeviceKey key, byte[] payload, byte[] associatedData)
	{
		var nonce = payload.AsSpan(0, NonceLength);
		var tag = payload.AsSpan(NonceLength, TagLength);
		var cipher = payload.AsSpan(NonceLength + TagLength);
		var plain = new byte[cipher.Length];
		try
		{
			using var aes = new AesGcm(key.Material.Span, TagLength);
			aes.Decrypt(nonce, cipher, tag, plain, associatedData);
		}
		catch (CryptographicException)
		{
			// The failed tag check is the expected answer for a changed or moved value, not a fault; the code says why.
			return Result.Failure<string>(DeviceDatabaseErrors.CiphertextRejected);
		}

		return Encoding.UTF8.GetString(plain);
	}
	#endregion
}
