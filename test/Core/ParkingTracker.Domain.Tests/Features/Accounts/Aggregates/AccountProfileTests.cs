using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;

namespace ParkingTracker.Domain.Tests.Features.Accounts.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="AccountProfile"/> and <see cref="EncryptedProfile"/>: the shape of the key identifier, the
///              size guards of the payload, a first revision of 1, a replacement that raises the revision and refuses a bad schema
///              version without changing anything.
/// </summary>
public sealed class AccountProfileTests
{
	#region Static Instances
	/// <summary>
	/// The day of the first write in the tests.
	/// </summary>
	private static readonly DateOnly Today = new(2026, 11, 26);
	#endregion

	#region Encrypted Profile Tests
	/// <summary>
	/// Verifies that a valid key identifier and payload are accepted, up to the largest payload of a 32 KB document.
	/// </summary>
	/// <param name="length">The payload length.</param>
	[Theory]
	[InlineData(1)]
	[InlineData(EncryptedProfile.MaximumPayloadBytes)]
	public void EncryptedProfile_Create_ValidValues_ShouldSucceed(int length)
	{
		var result = EncryptedProfile.Create("0a1b2c3d", new byte[length]);

		result.IsSuccess.Should().BeTrue();
		result.Value.KeyId.Should().Be("0a1b2c3d");
		result.Value.Payload.Should().HaveCount(length);
	}

	/// <summary>
	/// Verifies that a missing, short, long, upper-case or non-hexadecimal key identifier is refused.
	/// </summary>
	/// <param name="keyId">The key identifier to test.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("0a1b2c3")]
	[InlineData("0a1b2c3d4")]
	[InlineData("0A1B2C3D")]
	[InlineData("0a1b2c3g")]
	public void EncryptedProfile_Create_BadKeyId_ShouldFail(string? keyId)
	{
		EncryptedProfile.Create(keyId, [1]).Error.Should().Be(AccountErrors.ProfileKeyIdInvalid);
	}

	/// <summary>
	/// Verifies that a missing or empty payload is refused.
	/// </summary>
	[Fact]
	public void EncryptedProfile_Create_NoPayload_ShouldFail()
	{
		EncryptedProfile.Create("0a1b2c3d", null).Error.Should().Be(AccountErrors.ProfilePayloadEmpty);
		EncryptedProfile.Create("0a1b2c3d", []).Error.Should().Be(AccountErrors.ProfilePayloadEmpty);
	}

	/// <summary>
	/// Verifies that a payload above the size of a 32 KB document plus the cipher overhead is refused.
	/// </summary>
	[Fact]
	public void EncryptedProfile_Create_TooLargePayload_ShouldFail()
	{
		EncryptedProfile.Create("0a1b2c3d", new byte[EncryptedProfile.MaximumPayloadBytes + 1]).Error.Should().Be(AccountErrors.ProfilePayloadTooLarge);
	}

	/// <summary>
	/// Verifies that the value keeps its own copy of the payload, so a caller that changes its array cannot change what is stored.
	/// </summary>
	[Fact]
	public void EncryptedProfile_Create_ShouldCopyThePayload()
	{
		var bytes = new byte[] { 1, 2, 3 };

		var encrypted = EncryptedProfile.Create("0a1b2c3d", bytes).Value;
		bytes[0] = 9;

		encrypted.Payload.Should().Equal(1, 2, 3);
	}
	#endregion

	#region Create Tests
	/// <summary>
	/// Verifies that a new profile has the identifier of its account, revision 1 and the day of the write.
	/// </summary>
	[Fact]
	public void Create_NewProfile_ShouldStartAtRevisionOne()
	{
		var accountId = UserAccountId.New();

		var profile = AccountProfile.Create(accountId, 1, Encrypted(1), Today).Value;

		profile.Id.Should().Be(accountId);
		profile.Revision.Should().Be(1);
		profile.NextRevision.Should().Be(2);
		profile.SchemaVersion.Should().Be(1);
		profile.KeyId.Should().Be("0a1b2c3d");
		profile.Payload.Should().Equal(1);
		profile.UpdatedOn.Should().Be(Today);
	}

	/// <summary>
	/// Verifies that a schema version below 1 is refused.
	/// </summary>
	/// <param name="schemaVersion">The schema version to test.</param>
	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void Create_BadSchemaVersion_ShouldFail(int schemaVersion)
	{
		AccountProfile.Create(UserAccountId.New(), schemaVersion, Encrypted(1), Today).Error.Should().Be(AccountErrors.ProfileSchemaVersionInvalid);
	}

	/// <summary>
	/// Verifies that a missing account identifier or document is a programming error.
	/// </summary>
	[Fact]
	public void Create_NullArguments_ShouldThrow()
	{
		var noAccount = () => AccountProfile.Create(null!, 1, Encrypted(1), Today);
		var noDocument = () => AccountProfile.Create(UserAccountId.New(), 1, null!, Today);

		noAccount.Should().Throw<ArgumentNullException>();
		noDocument.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Replace Tests
	/// <summary>
	/// Verifies that a replacement raises the revision by one and takes the new document, schema version and day.
	/// </summary>
	[Fact]
	public void Replace_NewDocument_ShouldRaiseTheRevision()
	{
		var profile = AccountProfile.Create(UserAccountId.New(), 1, Encrypted(1), Today).Value;
		var next = EncryptedProfile.Create("ffeeddcc", [7, 8]).Value;

		var result = profile.Replace(2, next, Today.AddDays(3));

		result.IsSuccess.Should().BeTrue();
		profile.Revision.Should().Be(2);
		profile.NextRevision.Should().Be(3);
		profile.SchemaVersion.Should().Be(2);
		profile.KeyId.Should().Be("ffeeddcc");
		profile.Payload.Should().Equal(7, 8);
		profile.UpdatedOn.Should().Be(Today.AddDays(3));
	}

	/// <summary>
	/// Verifies that a refused replacement changes nothing.
	/// </summary>
	[Fact]
	public void Replace_BadSchemaVersion_ShouldFailAndKeepTheProfile()
	{
		var profile = AccountProfile.Create(UserAccountId.New(), 1, Encrypted(1), Today).Value;

		var result = profile.Replace(0, Encrypted(5), Today.AddDays(1));

		result.Error.Should().Be(AccountErrors.ProfileSchemaVersionInvalid);
		profile.Revision.Should().Be(1);
		profile.Payload.Should().Equal(1);
		profile.UpdatedOn.Should().Be(Today);
	}

	/// <summary>
	/// Verifies that a missing document is a programming error.
	/// </summary>
	[Fact]
	public void Replace_NullDocument_ShouldThrow()
	{
		var profile = AccountProfile.Create(UserAccountId.New(), 1, Encrypted(1), Today).Value;

		var act = () => profile.Replace(1, null!, Today);

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds an encrypted profile of one byte.
	/// </summary>
	/// <param name="value">The byte.</param>
	/// <returns>The encrypted profile.</returns>
	private static EncryptedProfile Encrypted(byte value) => EncryptedProfile.Create("0a1b2c3d", [value]).Value;
	#endregion
}
