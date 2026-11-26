using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A cipher for the handler tests that does not encrypt: it records what it was asked to encrypt, with the account, the
///              revision and the schema version, and answers a decryption with the result a test sets. A span cannot be matched by
///              a substitute library, so this is written by hand.
/// </summary>
internal sealed class FakeProfileCipher : IProfileCipher
{
	#region Properties
	/// <summary>
	/// Gets the calls to <see cref="Encrypt"/>, in order.
	/// </summary>
	public List<EncryptCall> EncryptCalls { get; } = [];

	/// <summary>
	/// Gets or sets the result of a decryption.
	/// </summary>
	public Result<byte[]> DecryptResult { get; set; } = Result.Success(Array.Empty<byte>());

	/// <summary>
	/// Gets the revisions a decryption was asked for, in order.
	/// </summary>
	public List<(UserAccountId Account, long Revision, int SchemaVersion)> DecryptCalls { get; } = [];
	#endregion

	#region IProfileCipher Implementation
	/// <inheritdoc />
	public EncryptedProfile Encrypt(UserAccountId accountId, long revision, int schemaVersion, ReadOnlySpan<byte> plaintext)
	{
		EncryptCalls.Add(new EncryptCall(accountId, revision, schemaVersion, plaintext.ToArray()));
		return EncryptedProfile.Create("0123abcd", [1, 2, 3]).Value;
	}

	/// <inheritdoc />
	public Result<byte[]> Decrypt(UserAccountId accountId, long revision, int schemaVersion, EncryptedProfile encrypted)
	{
		DecryptCalls.Add((accountId, revision, schemaVersion));
		return DecryptResult;
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : One request to encrypt.
	/// </summary>
	/// <param name="Account">The account.</param>
	/// <param name="Revision">The revision the document is written under.</param>
	/// <param name="SchemaVersion">The schema version.</param>
	/// <param name="Plain">The plain document.</param>
	public sealed record EncryptCall(UserAccountId Account, long Revision, int SchemaVersion, byte[] Plain);
	#endregion
}
