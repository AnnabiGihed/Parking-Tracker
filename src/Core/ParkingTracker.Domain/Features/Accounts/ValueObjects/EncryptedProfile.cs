using ParkingTracker.Domain.Features.Accounts.Errors;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.Accounts.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The profile document of an account as it is stored (ADR-0032): the identifier of the key that encrypted it and the
///              encrypted bytes. The domain never reads the payload; it only guards its size, so a profile of 32 KB of plain text
///              fits (the nonce and the tag of the cipher add <see cref="OverheadBytes"/>) and nothing larger is kept.
/// </summary>
public sealed record EncryptedProfile
{
	#region Constants
	/// <summary>
	/// The most plain text bytes a profile document has.
	/// </summary>
	public const int MaximumPlainBytes = 32 * 1024;

	/// <summary>
	/// The bytes that the encryption adds to the plain text: the 12-byte nonce in front and the 16-byte tag behind.
	/// </summary>
	public const int OverheadBytes = 28;

	/// <summary>
	/// The most bytes an encrypted profile has.
	/// </summary>
	public const int MaximumPayloadBytes = MaximumPlainBytes + OverheadBytes;

	/// <summary>
	/// The length of a key identifier written in hexadecimal.
	/// </summary>
	public const int KeyIdLength = 8;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="EncryptedProfile"/> class.
	/// </summary>
	/// <param name="keyId">The identifier of the key that encrypted the payload.</param>
	/// <param name="payload">The encrypted bytes.</param>
	private EncryptedProfile(string keyId, byte[] payload)
	{
		KeyId = keyId;
		Payload = payload;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the identifier of the key that encrypted the payload: 8 lowercase hexadecimal characters.
	/// </summary>
	public string KeyId { get; }

	/// <summary>
	/// Gets the encrypted bytes; opaque to the domain.
	/// </summary>
	public byte[] Payload { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the value from a key identifier and encrypted bytes.
	/// </summary>
	/// <param name="keyId">The identifier of the key that encrypted the payload.</param>
	/// <param name="payload">The encrypted bytes.</param>
	/// <returns>The value, or a failure when the key identifier has the wrong shape or the payload is empty or too large.</returns>
	public static Result<EncryptedProfile> Create(string? keyId, byte[]? payload)
	{
		if (keyId is not { Length: KeyIdLength } || !keyId.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
		{
			return Result.Failure<EncryptedProfile>(AccountErrors.ProfileKeyIdInvalid);
		}

		if (payload is null or { Length: 0 })
		{
			return Result.Failure<EncryptedProfile>(AccountErrors.ProfilePayloadEmpty);
		}

		return payload.Length > MaximumPayloadBytes
			? Result.Failure<EncryptedProfile>(AccountErrors.ProfilePayloadTooLarge)
			: new EncryptedProfile(keyId, [.. payload]);
	}
	#endregion
}
