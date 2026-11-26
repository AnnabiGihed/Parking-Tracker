using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.EndSession;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.EraseAccount;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="EndSessionCommandHandler"/> and <see cref="EraseAccountCommandHandler"/> with the ports
///              replaced: the sign-out revokes only the family of the caller and is idempotent; the erasure stages the log record, the deletion of the
///              profile and the deletion of the account together, saves once, and is idempotent; a failed save is reported.
/// </summary>
public sealed class EndSessionAndEraseAccountHandlerTests
{
	#region Fields
	/// <summary>
	/// The accounts.
	/// </summary>
	private readonly IUserAccountRepository accounts = Substitute.For<IUserAccountRepository>();

	/// <summary>
	/// The encrypted profiles.
	/// </summary>
	private readonly IAccountProfileRepository profiles = Substitute.For<IAccountProfileRepository>();

	/// <summary>
	/// The erasure log.
	/// </summary>
	private readonly IErasureLog erasureLog = Substitute.For<IErasureLog>();

	/// <summary>
	/// The unit of work.
	/// </summary>
	private readonly IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();

	/// <summary>
	/// The clock.
	/// </summary>
	private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 20, 23, 30, 0, TimeSpan.Zero));
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="EndSessionAndEraseAccountHandlerTests"/> class with a unit of work that saves.
	/// </summary>
	public EndSessionAndEraseAccountHandlerTests()
	{
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success()));
	}
	#endregion

	#region End Session Tests
	/// <summary>
	/// Verifies that the sign-out revokes the family of the caller, leaves another family alone and saves once.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EndSession_KnownFamily_ShouldRevokeOnlyThatFamilyAndSave()
	{
		var account = NewAccount();
		var mine = account.StartSession(RefreshTokenHash.Create(new string('1', 64)).Value, clock.GetUtcNow());
		var other = account.StartSession(RefreshTokenHash.Create(new string('2', 64)).Value, clock.GetUtcNow());

		var result = await new EndSessionCommandHandler(accounts, unitOfWork).Handle(new EndSessionCommand(account.Id.Value, mine.FamilyId), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		mine.Revoked.Should().BeTrue();
		other.Revoked.Should().BeFalse();
		await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that signing out of a family or an account that is gone succeeds and saves nothing.
	/// </summary>
	/// <param name="accountExists">Whether the account exists.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task EndSession_UnknownFamilyOrAccount_ShouldSucceedWithoutSaving(bool accountExists)
	{
		var account = NewAccount();
		_ = account.StartSession(RefreshTokenHash.Create(new string('1', 64)).Value, clock.GetUtcNow());
		if (!accountExists)
		{
			_ = accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns((UserAccount?)null);
		}

		var result = await new EndSessionCommandHandler(accounts, unitOfWork).Handle(new EndSessionCommand(account.Id.Value, Guid.NewGuid()), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
	}

	/// <summary>
	/// Verifies that a failed save of the revocation is reported and not hidden as a success.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task EndSession_SaveFails_ShouldReportTheFailure()
	{
		var account = NewAccount();
		var mine = account.StartSession(RefreshTokenHash.Create(new string('1', 64)).Value, clock.GetUtcNow());
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Database", "refused"), ResultExceptionType.Conflict)));

		var result = await new EndSessionCommandHandler(accounts, unitOfWork).Handle(new EndSessionCommand(account.Id.Value, mine.FamilyId), CancellationToken.None);

		result.IsFailure.Should().BeTrue();
	}
	#endregion

	#region Erase Account Tests
	/// <summary>
	/// Verifies that the erasure records the subject hash with the UTC day, deletes the account and saves once, so one transaction
	/// holds both.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_KnownAccount_ShouldStageTheLogAndTheDeletionAndSaveOnce()
	{
		var account = NewAccount();

		var result = await Handler().Handle(new EraseAccountCommand(account.Id.Value), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		await erasureLog.Received(1).RecordAsync(account.SubjectHash, new DateOnly(2026, 11, 20), Arg.Any<CancellationToken>());
		await accounts.Received(1).DeleteAsync(account, Arg.Any<CancellationToken>());
		await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the profile of the account is deleted in the same save as the account, and that an account without a profile
	/// deletes none.
	/// </summary>
	/// <param name="hasProfile">Whether the account has a profile.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Erase_AccountWithOrWithoutProfile_ShouldDeleteTheProfileInTheSameSave(bool hasProfile)
	{
		var account = NewAccount();
		var profile = AccountProfile.Create(account.Id, 1, EncryptedProfile.Create("0a1b2c3d", [1, 2, 3]).Value, new DateOnly(2026, 11, 20)).Value;
		_ = profiles.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(hasProfile ? profile : null);

		var result = await Handler().Handle(new EraseAccountCommand(account.Id.Value), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		await profiles.Received(hasProfile ? 1 : 0).DeleteAsync(profile, Arg.Any<CancellationToken>());
		await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that erasing an account that is gone succeeds and records and deletes nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_UnknownAccount_ShouldSucceedAndChangeNothing()
	{
		var result = await Handler().Handle(new EraseAccountCommand(Guid.NewGuid()), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		await erasureLog.DidNotReceiveWithAnyArgs().RecordAsync(default!, default, default);
		await accounts.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
		await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
	}

	/// <summary>
	/// Verifies that a failed save of the erasure is reported, so the caller does not tell the user it worked.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_SaveFails_ShouldReportTheFailure()
	{
		var account = NewAccount();
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Concurrency", "lost"), ResultExceptionType.Conflict)));

		var result = await Handler().Handle(new EraseAccountCommand(account.Id.Value), CancellationToken.None);

		result.IsFailure.Should().BeTrue();
		result.ResultExceptionType.Should().Be(ResultExceptionType.Conflict);
	}

	/// <summary>
	/// Verifies that a save that fails because a second request erased the account first is a success, since the account is gone.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Erase_SaveFailsBecauseTheAccountWasErasedMeanwhile_ShouldSucceed()
	{
		var account = NewAccount();
		_ = accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account, (UserAccount?)null);
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Concurrency", "lost"), ResultExceptionType.Conflict)));

		var result = await Handler().Handle(new EraseAccountCommand(account.Id.Value), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		await accounts.Received(2).FindByIdAsync(account.Id, Arg.Any<CancellationToken>());
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Creates the erase handler on the replaced ports.
	/// </summary>
	/// <returns>The handler.</returns>
	private EraseAccountCommandHandler Handler() => new(accounts, profiles, erasureLog, unitOfWork, clock);

	/// <summary>
	/// Makes an account and lets the repository find it by identifier.
	/// </summary>
	/// <returns>The account.</returns>
	private UserAccount NewAccount()
	{
		var account = UserAccount.Create(SubjectHash.Create(new string('b', 64)).Value, clock.GetUtcNow());
		_ = accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
		return account;
	}
	#endregion
}
