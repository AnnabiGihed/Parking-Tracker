using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ParkingTracker.ApiService.Features.Profiles;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;

namespace ParkingTracker.Api.E2E.Tests.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests of <see cref="ProfileCipher"/> without a host or a database (ADR-0032): the round trip, the layout of the
///              payload and the key identifier against an independent AES-GCM and SHA-256, the refusal of another account, another
///              revision, changed bytes and an unknown key, the decryption with the previous key, a fresh nonce per encryption, and
///              the size guards.
/// </summary>
public sealed class ProfileCipherTests
{
	#region Static Instances
	/// <summary>
	/// The current key of the tests.
	/// </summary>
	private static readonly byte[] CurrentKey = RandomNumberGenerator.GetBytes(32);

	/// <summary>
	/// The previous key of the tests.
	/// </summary>
	private static readonly byte[] PreviousKey = RandomNumberGenerator.GetBytes(32);

	/// <summary>
	/// The plain document of the tests.
	/// </summary>
	private static readonly byte[] Document = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"home\":null}");
	#endregion

	#region Round Trip Tests
	/// <summary>
	/// Verifies that a document comes back as it was encrypted.
	/// </summary>
	[Fact]
	public void Decrypt_AfterEncrypt_ShouldReturnTheDocument()
	{
		var cipher = Cipher(CurrentKey, null);
		var account = UserAccountId.New();

		var result = cipher.Decrypt(account, 3, 1, cipher.Encrypt(account, 3, 1, Document));

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().Equal(Document);
	}

	/// <summary>
	/// Verifies the payload layout (12-byte nonce, cipher text, 16-byte tag), the associated data and the key identifier against
	/// an independent AES-GCM and SHA-256, and that the document is not readable in the payload.
	/// </summary>
	[Fact]
	public void Encrypt_ShouldFollowTheDocumentedFormat()
	{
		var account = UserAccountId.New();

		var encrypted = Cipher(CurrentKey, null).Encrypt(account, 7, 1, Document);

		encrypted.KeyId.Should().Be(Convert.ToHexStringLower(SHA256.HashData(CurrentKey))[..8]);
		encrypted.Payload.Should().HaveCount(12 + Document.Length + 16);
		var plain = new byte[Document.Length];
		using var aes = new AesGcm(CurrentKey, 16);
		aes.Decrypt(encrypted.Payload.AsSpan(0, 12), encrypted.Payload.AsSpan(12, Document.Length), encrypted.Payload.AsSpan(12 + Document.Length, 16), plain, Encoding.UTF8.GetBytes($"pt-profile|v1|{account.Value:D}|7|1"));
		plain.Should().Equal(Document);
		encrypted.Payload.AsSpan(12, Document.Length).SequenceEqual(Document).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that two encryptions of the same document differ, because every one has its own nonce.
	/// </summary>
	[Fact]
	public void Encrypt_SameDocumentTwice_ShouldGiveDifferentPayloads()
	{
		var cipher = Cipher(CurrentKey, null);
		var account = UserAccountId.New();

		cipher.Encrypt(account, 1, 1, Document).Payload.Should().NotEqual(cipher.Encrypt(account, 1, 1, Document).Payload);
	}
	#endregion

	#region Refusal Tests
	/// <summary>
	/// Verifies that a payload moved to another account is refused.
	/// </summary>
	[Fact]
	public void Decrypt_OtherAccount_ShouldBeUnreadable()
	{
		var cipher = Cipher(CurrentKey, null);
		var encrypted = cipher.Encrypt(UserAccountId.New(), 1, 1, Document);

		cipher.Decrypt(UserAccountId.New(), 1, 1, encrypted).Error.Should().Be(ProfileErrors.Unreadable);
	}

	/// <summary>
	/// Verifies that a payload replayed under another revision is refused.
	/// </summary>
	[Fact]
	public void Decrypt_OtherRevision_ShouldBeUnreadable()
	{
		var cipher = Cipher(CurrentKey, null);
		var account = UserAccountId.New();
		var encrypted = cipher.Encrypt(account, 1, 1, Document);

		cipher.Decrypt(account, 2, 1, encrypted).Error.Should().Be(ProfileErrors.Unreadable);
	}

	/// <summary>
	/// Verifies that a changed nonce, cipher text or tag is refused, and that a payload too short to hold a nonce and a tag is too.
	/// </summary>
	/// <param name="position">The position of the byte to change; negative for a truncated payload.</param>
	[Theory]
	[InlineData(0)]
	[InlineData(12)]
	[InlineData(-1)]
	public void Decrypt_TamperedPayload_ShouldBeUnreadable(int position)
	{
		var cipher = Cipher(CurrentKey, null);
		var account = UserAccountId.New();
		var encrypted = cipher.Encrypt(account, 1, 1, Document);
		var bytes = encrypted.Payload;
		var damaged = position >= 0 ? Flip(bytes, position) : bytes[..20];
		var tampered = EncryptedProfile.Create(encrypted.KeyId, damaged).Value;

		cipher.Decrypt(account, 1, 1, tampered).Error.Should().Be(ProfileErrors.Unreadable);
	}

	/// <summary>
	/// Verifies that a changed last byte, in the tag, is refused.
	/// </summary>
	[Fact]
	public void Decrypt_TamperedTag_ShouldBeUnreadable()
	{
		var cipher = Cipher(CurrentKey, null);
		var account = UserAccountId.New();
		var encrypted = cipher.Encrypt(account, 1, 1, Document);
		var tampered = EncryptedProfile.Create(encrypted.KeyId, Flip(encrypted.Payload, encrypted.Payload.Length - 1)).Value;

		cipher.Decrypt(account, 1, 1, tampered).Error.Should().Be(ProfileErrors.Unreadable);
	}

	/// <summary>
	/// Verifies that a payload of a key that is neither the current nor the previous one is refused as an unknown key.
	/// </summary>
	[Fact]
	public void Decrypt_UnknownKeyId_ShouldFailAsKeyUnknown()
	{
		var account = UserAccountId.New();
		var encrypted = Cipher(PreviousKey, null).Encrypt(account, 1, 1, Document);

		Cipher(CurrentKey, null).Decrypt(account, 1, 1, encrypted).Error.Should().Be(ProfileErrors.KeyUnknown);
	}

	/// <summary>
	/// Verifies that a payload kept under another schema version is refused.
	/// </summary>
	[Fact]
	public void Decrypt_OtherSchemaVersion_ShouldBeUnreadable()
	{
		var cipher = Cipher(CurrentKey, null);
		var account = UserAccountId.New();
		var encrypted = cipher.Encrypt(account, 1, 1, Document);

		cipher.Decrypt(account, 1, 2, encrypted).Error.Should().Be(ProfileErrors.Unreadable);
	}
	#endregion

	#region Rotation Tests
	/// <summary>
	/// Verifies that after a rotation a payload of the previous key still decrypts, and that new payloads use the current key.
	/// </summary>
	[Fact]
	public void Decrypt_PayloadOfThePreviousKey_ShouldSucceedAndNewOnesUseTheCurrentKey()
	{
		var account = UserAccountId.New();
		var old = Cipher(PreviousKey, null).Encrypt(account, 1, 1, Document);
		var rotated = Cipher(CurrentKey, PreviousKey);

		rotated.Decrypt(account, 1, 1, old).Value.Should().Equal(Document);
		rotated.Encrypt(account, 2, 1, Document).KeyId.Should().Be(Cipher(CurrentKey, null).Encrypt(account, 2, 1, Document).KeyId).And.NotBe(old.KeyId);
	}

	/// <summary>
	/// Verifies that without a configured key (Development) the cipher still works, with a key of its own.
	/// </summary>
	[Fact]
	public void Cipher_NoKeyConfigured_ShouldUseAProcessKey()
	{
		var cipher = new ProfileCipher(Options.Create(new ProfileOptions()));
		var account = UserAccountId.New();

		cipher.Decrypt(account, 1, 1, cipher.Encrypt(account, 1, 1, Document)).Value.Should().Equal(Document);
	}
	#endregion

	#region Guard Tests
	/// <summary>
	/// Verifies that the largest document of 32 KB fits the stored size and that an empty or a larger one is refused.
	/// </summary>
	[Fact]
	public void Encrypt_Sizes_ShouldAcceptTheLimitAndRefuseOutsideIt()
	{
		var cipher = Cipher(CurrentKey, null);
		var account = UserAccountId.New();

		cipher.Encrypt(account, 1, 1, new byte[EncryptedProfile.MaximumPlainBytes]).Payload.Should().HaveCount(EncryptedProfile.MaximumPayloadBytes);
		var empty = () => cipher.Encrypt(account, 1, 1, []);
		var large = () => cipher.Encrypt(account, 1, 1, new byte[EncryptedProfile.MaximumPlainBytes + 1]);
		var revision = () => cipher.Encrypt(account, 0, 1, Document);
		empty.Should().Throw<ArgumentException>();
		large.Should().Throw<ArgumentException>();
		revision.Should().Throw<ArgumentOutOfRangeException>();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a cipher on keys.
	/// </summary>
	/// <param name="current">The current key.</param>
	/// <param name="previous">The previous key, or <see langword="null"/>.</param>
	/// <returns>The cipher.</returns>
	private static ProfileCipher Cipher(byte[] current, byte[]? previous)
	{
		var options = new ProfileOptions
		{
			Keys = new ProfileKeyOptions { Current = Convert.ToBase64String(current), Previous = previous is null ? null : Convert.ToBase64String(previous) },
		};
		return new ProfileCipher(Options.Create(options));
	}

	/// <summary>
	/// Copies bytes with one of them changed.
	/// </summary>
	/// <param name="bytes">The bytes.</param>
	/// <param name="position">The position to change.</param>
	/// <returns>The changed copy.</returns>
	private static byte[] Flip(byte[] bytes, int position)
	{
		var copy = (byte[])bytes.Clone();
		copy[position] ^= 0xFF;
		return copy;
	}
	#endregion
}
