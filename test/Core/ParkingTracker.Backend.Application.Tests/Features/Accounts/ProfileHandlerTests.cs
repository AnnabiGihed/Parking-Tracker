using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.SaveProfile;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Backend.Application.Features.Accounts.Queries.GetProfile;
using ParkingTracker.Backend.Application.Features.Accounts.Services;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests of <see cref="SaveProfileCommandHandler"/> and <see cref="GetProfileQueryHandler"/> with the ports replaced
///              (ADR-0032): the encryption under the revision the row will have, the preconditions of a create and of a replacement,
///              a lost save being a failed precondition, an unknown account being an authentication failure, and a profile that cannot
///              be read failing without its cause.
/// </summary>
public sealed class ProfileHandlerTests
{
	#region Fields
	/// <summary>
	/// The clock.
	/// </summary>
	private readonly FakeTimeProvider clock = new(ProfileTestData.Now);

	/// <summary>
	/// The accounts.
	/// </summary>
	private readonly IUserAccountRepository accounts = Substitute.For<IUserAccountRepository>();

	/// <summary>
	/// The encrypted profiles.
	/// </summary>
	private readonly IAccountProfileRepository profiles = Substitute.For<IAccountProfileRepository>();

	/// <summary>
	/// The cipher, which records what it is asked.
	/// </summary>
	private readonly FakeProfileCipher cipher = new();

	/// <summary>
	/// The unit of work.
	/// </summary>
	private readonly IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();

	/// <summary>
	/// The account of the tests.
	/// </summary>
	private readonly UserAccount account;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileHandlerTests"/> class.
	/// </summary>
	public ProfileHandlerTests()
	{
		account = UserAccount.Create(SubjectHash.Create(new string('c', 64)).Value, clock.GetUtcNow());
		_ = accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success()));
	}
	#endregion

	#region Create Tests
	/// <summary>
	/// Verifies that a create encrypts under revision 1, stores a profile at revision 1 and answers revision 1.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_CreateWithoutProfile_ShouldEncryptUnderRevisionOneAndStore()
	{
		var result = await Save(ProfileWriteCondition.CreateOnly);

		result.IsSuccess.Should().BeTrue();
		result.Value.Revision.Should().Be(1);
		cipher.EncryptCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Account = account.Id, Revision = 1L, SchemaVersion = 1 }, options => options.ExcludingMissingMembers());
		await profiles.Received(1).AddAsync(Arg.Is<AccountProfile>(profile => profile.Id == account.Id && profile.Revision == 1 && profile.UpdatedOn == new DateOnly(2026, 11, 20)), Arg.Any<CancellationToken>());
		await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the moments of cleared sections survive the JSON round trip, and that a stored document without them reads back without.
	/// </summary>
	[Fact]
	public void Json_ShouldKeepTheClearedSectionsAndReadADocumentWithoutThem()
	{
		var cleared = new ParkingTracker.Contracts.Features.Profiles.ProfileClearedSections(ProfileTestData.Now, null, ProfileTestData.Now.AddMinutes(1), null);
		var document = ProfileTestData.Valid() with { Home = null, Vehicle = null, Cleared = cleared };

		var bytes = ProfileDocumentJson.Serialize(document);
		var back = ProfileDocumentJson.TryDeserialize(bytes);
		var old = ProfileDocumentJson.TryDeserialize("{\"schemaVersion\":1,\"favourites\":{\"updatedAt\":\"2026-11-20T10:00:00+00:00\",\"places\":[]}}"u8);

		System.Text.Encoding.UTF8.GetString(bytes).Should().Contain("\"cleared\":{");
		back!.Cleared.Should().Be(cleared);
		old!.Cleared.Should().BeNull();
	}

	/// <summary>
	/// Verifies that the plain document that is encrypted is the fixed camelCase JSON of the document, with nulls written.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_ShouldEncryptTheCamelCaseJsonOfTheDocument()
	{
		_ = await Save(ProfileWriteCondition.CreateOnly);

		var seen = cipher.EncryptCalls.Single().Plain;
		seen.Should().Equal(ProfileDocumentJson.Serialize(ProfileTestData.Valid()));
		System.Text.Encoding.UTF8.GetString(seen).Should().StartWith("{\"schemaVersion\":1,\"home\":{");
	}

	/// <summary>
	/// Verifies that a create over an existing profile is a failed precondition and changes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_CreateOverExistingProfile_ShouldFailThePrecondition()
	{
		StoreProfile(revision: 2);

		var result = await Save(ProfileWriteCondition.CreateOnly);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(ProfileErrors.PreconditionFailed);
		await profiles.DidNotReceive().AddAsync(Arg.Any<AccountProfile>(), Arg.Any<CancellationToken>());
		await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that two creates that both found no profile and the second one failing the save, with a conflict from the unit of
	/// work, fail the precondition and not as a server error.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_CreateThatLosesTheKeyRace_ShouldFailThePrecondition()
	{
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Database", "refused"), ResultExceptionType.Conflict)));

		var result = await Save(ProfileWriteCondition.CreateOnly);

		result.Error.Should().Be(ProfileErrors.PreconditionFailed);
		result.ResultExceptionType.Should().Be(ResultExceptionType.Conflict);
	}
	#endregion

	#region Replace Tests
	/// <summary>
	/// Verifies that a replacement at the stored revision encrypts under the next revision, replaces and answers it.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_ReplaceAtStoredRevision_ShouldEncryptUnderTheNextRevision()
	{
		var stored = StoreProfile(revision: 2);

		var result = await Save(ProfileWriteCondition.ForRevision(2));

		result.IsSuccess.Should().BeTrue();
		result.Value.Revision.Should().Be(3);
		stored.Revision.Should().Be(3);
		cipher.EncryptCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Account = account.Id, Revision = 3L, SchemaVersion = 1 }, options => options.ExcludingMissingMembers());
		await profiles.Received(1).UpdateAsync(stored, Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a replacement at another revision, or without a profile, is a failed precondition and changes nothing.
	/// </summary>
	/// <param name="stored">The stored revision, 0 for none.</param>
	/// <param name="expected">The revision the write names.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(3, 2)]
	[InlineData(2, 3)]
	[InlineData(0, 1)]
	public async Task Save_ReplaceAtOtherRevisionOrWithoutProfile_ShouldFailThePrecondition(int stored, int expected)
	{
		var profile = stored == 0 ? null : StoreProfile(stored);

		var result = await Save(ProfileWriteCondition.ForRevision(expected));

		result.Error.Should().Be(ProfileErrors.PreconditionFailed);
		profile?.Revision.Should().Be(stored);
		await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a replacement whose save loses to another write, the concurrency conflict of the revision, fails the precondition.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_ReplaceThatLosesTheSave_ShouldFailThePrecondition()
	{
		_ = StoreProfile(revision: 1);
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Concurrency", "lost"), ResultExceptionType.Conflict)));

		var result = await Save(ProfileWriteCondition.ForRevision(1));

		result.Error.Should().Be(ProfileErrors.PreconditionFailed);
	}

	/// <summary>
	/// Verifies that a save that fails for another reason than a conflict passes its failure on.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_WhenTheSaveFailsOtherwise_ShouldPassTheFailureOn()
	{
		var failure = new Error("Persistence.Other", "no");
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(failure, ResultExceptionType.None)));

		var result = await Save(ProfileWriteCondition.CreateOnly);

		result.Error.Should().Be(failure);
	}
	#endregion

	#region Account Tests
	/// <summary>
	/// Verifies that an unknown or empty account is an authentication failure for a write, and nothing is encrypted or stored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_UnknownOrEmptyAccount_ShouldBeAnAuthenticationFailure()
	{
		var handler = NewSaveHandler();

		var unknown = await handler.Handle(new SaveProfileCommand(Guid.NewGuid(), ProfileTestData.Valid(), ProfileWriteCondition.CreateOnly), CancellationToken.None);
		var empty = await handler.Handle(new SaveProfileCommand(Guid.Empty, ProfileTestData.Valid(), ProfileWriteCondition.CreateOnly), CancellationToken.None);

		unknown.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
		empty.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
		unknown.Error.Should().Be(SessionErrors.AccountUnknown);
		await profiles.DidNotReceive().AddAsync(Arg.Any<AccountProfile>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a document that is larger than a profile may be when written is refused before anything is encrypted.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Save_DocumentLargerThanTheLimit_ShouldBeRefusedBeforeEncrypting()
	{
		var huge = ProfileTestData.Valid() with { Home = ProfileTestData.Place() with { AddressText = new string('a', 40000) } };

		var result = await NewSaveHandler().Handle(new SaveProfileCommand(account.Id.Value, huge, ProfileWriteCondition.CreateOnly), CancellationToken.None);

		result.Error.Should().Be(ProfileErrors.TooLarge);
		result.ResultExceptionType.Should().Be(ResultExceptionType.ValidationError);
		cipher.EncryptCalls.Should().BeEmpty();
	}
	#endregion

	#region Read Tests
	/// <summary>
	/// Verifies that a stored profile is decrypted under the revision, schema version and account it is stored with, and read back
	/// as the document with its revision.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_StoredProfile_ShouldReturnTheDocumentAndTheRevision()
	{
		var stored = StoreProfile(revision: 3);
		cipher.DecryptResult = Result.Success(ProfileDocumentJson.Serialize(ProfileTestData.Valid()));

		var result = await NewGetHandler().Handle(new GetProfileQuery(account.Id.Value), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.Revision.Should().Be(stored.Revision);
		result.Value.Document.Should().BeEquivalentTo(ProfileTestData.Valid());
		cipher.DecryptCalls.Should().ContainSingle().Which.Should().Be((account.Id, 3L, 1));
	}

	/// <summary>
	/// Verifies that an account without a profile is a not-found, and an unknown account an authentication failure.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_WithoutProfileOrWithoutAccount_ShouldBeNotFoundOrAuthenticationFailure()
	{
		var handler = NewGetHandler();

		var none = await handler.Handle(new GetProfileQuery(account.Id.Value), CancellationToken.None);
		var unknown = await handler.Handle(new GetProfileQuery(Guid.NewGuid()), CancellationToken.None);
		var empty = await handler.Handle(new GetProfileQuery(Guid.Empty), CancellationToken.None);

		none.Error.Should().Be(ProfileErrors.NotFound);
		none.ResultExceptionType.Should().Be(ResultExceptionType.NotFound);
		unknown.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
		empty.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
	}

	/// <summary>
	/// Verifies that a refused decryption passes its error on, and that bytes that decrypt but are not a document are unreadable.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Get_UnreadableProfile_ShouldFailWithoutTheCause()
	{
		_ = StoreProfile(revision: 1);
		var handler = NewGetHandler();
		cipher.DecryptResult = Result.Failure<byte[]>(ProfileErrors.KeyUnknown);

		var unknownKey = await handler.Handle(new GetProfileQuery(account.Id.Value), CancellationToken.None);
		cipher.DecryptResult = Result.Success(System.Text.Encoding.UTF8.GetBytes("not json"));
		var notJson = await handler.Handle(new GetProfileQuery(account.Id.Value), CancellationToken.None);

		unknownKey.Error.Should().Be(ProfileErrors.KeyUnknown);
		notJson.Error.Should().Be(ProfileErrors.Unreadable);
		notJson.Error.Message.Should().NotContain("json");
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Sends a save of the valid document under a condition.
	/// </summary>
	/// <param name="condition">The condition of the write.</param>
	/// <returns>The result of the handler.</returns>
	private Task<Result<SaveProfileResponse>> Save(ProfileWriteCondition condition)
	{
		return NewSaveHandler().Handle(new SaveProfileCommand(account.Id.Value, ProfileTestData.Valid(), condition), CancellationToken.None);
	}

	/// <summary>
	/// Builds the save handler on the fake ports.
	/// </summary>
	/// <returns>The handler.</returns>
	private SaveProfileCommandHandler NewSaveHandler()
	{
		return new SaveProfileCommandHandler(accounts, profiles, cipher, unitOfWork, clock);
	}

	/// <summary>
	/// Builds the read handler on the fake ports.
	/// </summary>
	/// <returns>The handler.</returns>
	private GetProfileQueryHandler NewGetHandler()
	{
		return new GetProfileQueryHandler(accounts, profiles, cipher, NullLogger<GetProfileQueryHandler>.Instance);
	}

	/// <summary>
	/// Makes the profile repository hold a profile of the account at a revision.
	/// </summary>
	/// <param name="revision">The revision, 1 or more.</param>
	/// <returns>The stored profile.</returns>
	private AccountProfile StoreProfile(int revision)
	{
		var encrypted = EncryptedProfile.Create("0123abcd", [1, 2, 3]).Value;
		var profile = AccountProfile.Create(account.Id, 1, encrypted, new DateOnly(2026, 11, 1)).Value;
		for (var step = 1; step < revision; step++)
		{
			_ = profile.Replace(1, encrypted, new DateOnly(2026, 11, 1));
		}

		_ = profiles.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(profile);
		return profile;
	}
	#endregion
}
