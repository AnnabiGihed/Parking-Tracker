using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that encrypts and decrypts the profile document of an account with the server's profile key (ADR-0032). The
///              account, the revision and the schema version are authenticated with the document, so a payload moved to another account or
///              kept under another revision or schema version is refused.
/// </summary>
public interface IProfileCipher
{
	#region Methods
	/// <summary>
	/// Encrypts a profile document under the current key.
	/// </summary>
	/// <param name="accountId">The account the profile belongs to.</param>
	/// <param name="revision">The revision the document is written under.</param>
	/// <param name="schemaVersion">The schema version of the plain document.</param>
	/// <param name="plaintext">The plain document.</param>
	/// <returns>The key identifier and the encrypted bytes.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="accountId"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="plaintext"/> is empty or larger than 32 KB.</exception>
	EncryptedProfile Encrypt(UserAccountId accountId, long revision, int schemaVersion, ReadOnlySpan<byte> plaintext);

	/// <summary>
	/// Decrypts a stored profile document with the key that encrypted it.
	/// </summary>
	/// <param name="accountId">The account the profile belongs to.</param>
	/// <param name="revision">The revision the profile is stored under.</param>
	/// <param name="schemaVersion">The schema version stored with the profile.</param>
	/// <param name="encrypted">The stored key identifier and encrypted bytes.</param>
	/// <returns>The plain document, or a failure when the key is not configured or the bytes are not the document of this account and revision.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="accountId"/> or <paramref name="encrypted"/> is <see langword="null"/>.</exception>
	Result<byte[]> Decrypt(UserAccountId accountId, long revision, int schemaVersion, EncryptedProfile encrypted);
	#endregion
}
