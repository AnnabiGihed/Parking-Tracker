using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.ApiService.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Encrypts the profile document with AES-256-GCM (ADR-0032). The payload is the 12-byte random nonce, the cipher text
///              and the 16-byte tag. The associated data is <c>pt-profile|v1|{account}|{revision}|{schemaVersion}</c>, so a payload cannot be moved to
///              another account or kept under another revision. A key is named by the first 8 hexadecimal characters of its SHA-256;
///              the current key encrypts, and the current or the previous key decrypts, chosen by that name. Without a configured key
///              (Development only) it uses a random key for the life of the process; the options validator refuses that anywhere else.
/// </summary>
public sealed class ProfileCipher : IProfileCipher
{
	#region Constants
	/// <summary>
	/// The size of the nonce in bytes.
	/// </summary>
	private const int NonceBytes = 12;

	/// <summary>
	/// The size of the authentication tag in bytes.
	/// </summary>
	private const int TagBytes = 16;
	#endregion

	#region Fields
	/// <summary>
	/// The current key, which encrypts.
	/// </summary>
	private readonly byte[] currentKey;

	/// <summary>
	/// The identifier of the current key.
	/// </summary>
	private readonly string currentKeyId;

	/// <summary>
	/// The previous key, or <see langword="null"/> when there is none.
	/// </summary>
	private readonly byte[]? previousKey;

	/// <summary>
	/// The identifier of the previous key, or <see langword="null"/> when there is none.
	/// </summary>
	private readonly string? previousKeyId;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileCipher"/> class.
	/// </summary>
	/// <param name="options">The profile settings holding the keys as base64.</param>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="FormatException">A configured key is not base64.</exception>
	public ProfileCipher(IOptions<ProfileOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);
		var keys = options.Value.Keys;
		currentKey = string.IsNullOrWhiteSpace(keys.Current)
			? RandomNumberGenerator.GetBytes(ProfileOptions.KeyBytes)
			: Convert.FromBase64String(keys.Current);
		currentKeyId = IdOf(currentKey);
		if (!string.IsNullOrWhiteSpace(keys.Previous))
		{
			previousKey = Convert.FromBase64String(keys.Previous);
			previousKeyId = IdOf(previousKey);
		}
	}
	#endregion

	#region IProfileCipher Implementation
	/// <inheritdoc />
	public EncryptedProfile Encrypt(UserAccountId accountId, long revision, int schemaVersion, ReadOnlySpan<byte> plaintext)
	{
		ArgumentNullException.ThrowIfNull(accountId);
		ArgumentOutOfRangeException.ThrowIfLessThan(revision, AccountProfile.FirstRevision);
		if (plaintext.Length is 0 or > EncryptedProfile.MaximumPlainBytes)
		{
			throw new ArgumentException($"A profile document has 1 to {EncryptedProfile.MaximumPlainBytes} bytes.", nameof(plaintext));
		}

		var payload = new byte[NonceBytes + plaintext.Length + TagBytes];
		var nonce = payload.AsSpan(0, NonceBytes);
		RandomNumberGenerator.Fill(nonce);
		using var aes = new AesGcm(currentKey, TagBytes);
		aes.Encrypt(nonce, plaintext, payload.AsSpan(NonceBytes, plaintext.Length), payload.AsSpan(NonceBytes + plaintext.Length, TagBytes), AssociatedData(accountId, revision, schemaVersion));
		return EncryptedProfile.Create(currentKeyId, payload).Value;
	}

	/// <inheritdoc />
	public Result<byte[]> Decrypt(UserAccountId accountId, long revision, int schemaVersion, EncryptedProfile encrypted)
	{
		ArgumentNullException.ThrowIfNull(accountId);
		ArgumentNullException.ThrowIfNull(encrypted);
		var key = KeyFor(encrypted.KeyId);
		if (key is null)
		{
			return Result.Failure<byte[]>(ProfileErrors.KeyUnknown);
		}

		var payload = encrypted.Payload;
		if (payload.Length <= NonceBytes + TagBytes)
		{
			return Result.Failure<byte[]>(ProfileErrors.Unreadable);
		}

		var length = payload.Length - NonceBytes - TagBytes;
		var plaintext = new byte[length];
		try
		{
			using var aes = new AesGcm(key, TagBytes);
			aes.Decrypt(payload.AsSpan(0, NonceBytes), payload.AsSpan(NonceBytes, length), payload.AsSpan(NonceBytes + length, TagBytes), plaintext, AssociatedData(accountId, revision, schemaVersion));
		}
		catch (CryptographicException)
		{
			// A wrong account, revision, key or changed bytes all end here; the caller learns only that the profile is unreadable.
			return Result.Failure<byte[]>(ProfileErrors.Unreadable);
		}

		return plaintext;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Picks the key that has an identifier.
	/// </summary>
	/// <param name="keyId">The identifier stored with the profile.</param>
	/// <returns>The key, or <see langword="null"/> when it is neither the current nor the previous key.</returns>
	private byte[]? KeyFor(string keyId)
	{
		if (string.Equals(keyId, currentKeyId, StringComparison.Ordinal))
		{
			return currentKey;
		}

		return string.Equals(keyId, previousKeyId, StringComparison.Ordinal) ? previousKey : null;
	}

	/// <summary>
	/// Names a key: the first 8 lowercase hexadecimal characters of its SHA-256.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <returns>The identifier.</returns>
	private static string IdOf(byte[] key) => Convert.ToHexStringLower(SHA256.HashData(key))[..EncryptedProfile.KeyIdLength];

	/// <summary>
	/// Builds the associated data that binds a payload to its account and revision.
	/// </summary>
	/// <param name="accountId">The account.</param>
	/// <param name="revision">The revision.</param>
	/// <param name="schemaVersion">The schema version of the plain document.</param>
	/// <returns>The UTF-8 bytes of <c>pt-profile|v1|{account}|{revision}|{schemaVersion}</c>.</returns>
	private static byte[] AssociatedData(UserAccountId accountId, long revision, int schemaVersion)
	{
		return Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"pt-profile|v1|{accountId.Value:D}|{revision}|{schemaVersion}"));
	}
	#endregion
}
