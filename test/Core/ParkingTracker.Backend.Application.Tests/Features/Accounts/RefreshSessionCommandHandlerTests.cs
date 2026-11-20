using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.RefreshSession;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Backend.Application.Features.Accounts.Services;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="RefreshSessionCommandHandler"/> with the ports replaced: a rotation is saved and answered
///              with new tokens of the same family; the reuse of a rotated token saves the revocation before the refusal; and every
///              other refusal (blank, unknown, revoked, expired, lost save) is the same authentication failure that logs a reason
///              code and never the token.
/// </summary>
public sealed class RefreshSessionCommandHandlerTests
{
	#region Constants
	/// <summary>
	/// The refresh token the tests present.
	/// </summary>
	private const string Presented = "the-presented-refresh-token";
	#endregion

	#region Fields
	/// <summary>
	/// The accounts.
	/// </summary>
	private readonly IUserAccountRepository accounts = Substitute.For<IUserAccountRepository>();

	/// <summary>
	/// The unit of work.
	/// </summary>
	private readonly IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();

	/// <summary>
	/// The access token issuer.
	/// </summary>
	private readonly IAccessTokenIssuer issuer = Substitute.For<IAccessTokenIssuer>();

	/// <summary>
	/// The clock.
	/// </summary>
	private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ListLogger<RefreshSessionCommandHandler> logger = new();
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="RefreshSessionCommandHandlerTests"/> class with ports that let a refresh succeed.
	/// </summary>
	public RefreshSessionCommandHandlerTests()
	{
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success()));
		_ = issuer.Issue(Arg.Any<UserAccountId>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()).Returns(call => new IssuedAccessToken("the-access-token", call.Arg<DateTimeOffset>().AddMinutes(15)));
	}
	#endregion

	#region Success Tests
	/// <summary>
	/// Verifies that a refresh rotates the token, saves once, issues the access token for the family of the session and returns a
	/// new refresh token whose hash is the one stored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ValidToken_ShouldRotateSaveAndReturnNewTokensOfTheSameFamily()
	{
		var account = AccountHolding(Presented, out var first);

		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.AccessToken.Should().Be("the-access-token");
		result.Value.RefreshToken.Should().MatchRegex("^[A-Za-z0-9_-]{43}$").And.NotBe(Presented);
		first.RotatedOn.Should().NotBeNull();
		var next = account.Sessions.Single(session => session != first);
		next.FamilyId.Should().Be(first.FamilyId);
		next.TokenHash.Value.Should().Be(RandomToken.Hash(result.Value.RefreshToken));
		await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
		issuer.Received(1).Issue(account.Id, first.FamilyId, clock.GetUtcNow());
		logger.Lines.Should().BeEmpty();
	}
	#endregion

	#region Refusal Tests
	/// <summary>
	/// Verifies that a token that was rotated before revokes its family, saves that revocation before refusing, and issues nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ReusedToken_ShouldSaveTheRevocationAndRefuse()
	{
		var account = AccountHolding(Presented, out _);
		_ = account.Rotate(RefreshTokenHash.Create(RandomToken.Hash(Presented)).Value, RefreshTokenHash.Create(new string('c', 64)).Value, clock.GetUtcNow());

		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		AssertRefused(result, RefreshFailureReason.TokenReused);
		account.Sessions.Should().OnlyContain(session => session.Revoked, "the whole family is revoked");
		await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
		issuer.DidNotReceiveWithAnyArgs().Issue(default!, Guid.Empty, default);
	}

	/// <summary>
	/// Verifies that when saving the revocation of a reused token fails, the account is read again and its family is revoked a second
	/// time, so the revocation is not lost to a race; the answer is the same refusal.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ReusedTokenAndTheFirstSaveFails_ShouldReloadAndRevokeOnceMore()
	{
		var account = AccountHolding(Presented, out var first);
		var presentedHash = RefreshTokenHash.Create(RandomToken.Hash(Presented)).Value;
		_ = account.Rotate(presentedHash, RefreshTokenHash.Create(new string('c', 64)).Value, clock.GetUtcNow());
		_ = accounts.ReloadByRefreshTokenHashAsync(presentedHash, Arg.Any<CancellationToken>()).Returns(account);
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Failure(new Error("Persistence.Concurrency", "lost"), ResultExceptionType.Conflict)), Task.FromResult(Result.Success()));

		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		AssertRefused(result, RefreshFailureReason.TokenReused);
		await accounts.Received(1).ReloadByRefreshTokenHashAsync(presentedHash, Arg.Any<CancellationToken>());
		await unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
		first.Revoked.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that when the revocation cannot be saved the second time either, the refusal is the same and the failure is logged
	/// with a reason code and without any token data.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ReusedTokenAndBothSavesFail_ShouldStillRefuseAndLogTheFailure()
	{
		var account = AccountHolding(Presented, out _);
		var presentedHash = RefreshTokenHash.Create(RandomToken.Hash(Presented)).Value;
		_ = account.Rotate(presentedHash, RefreshTokenHash.Create(new string('c', 64)).Value, clock.GetUtcNow());
		_ = accounts.ReloadByRefreshTokenHashAsync(presentedHash, Arg.Any<CancellationToken>()).Returns(account);
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Concurrency", "lost"), ResultExceptionType.Conflict)));

		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		result.Error.Should().Be(SessionErrors.RefreshFailed);
		result.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
		await unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
		logger.Lines.Should().Equal(
			"Warning: Refresh revocation not saved: refresh.revocation-unsaved",
			"Warning: Refresh refused: refresh.reused");
		logger.Lines.Should().NotContain(line => line.Contains(Presented, StringComparison.Ordinal));
	}

	/// <summary>
	/// Verifies that a database error while the account is read again still ends in the generic refusal, and that the log has the
	/// reason code and the type of the exception only, not its message.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ReusedTokenAndTheReloadThrows_ShouldStillRefuseAndLogTheExceptionType()
	{
		var account = AccountHolding(Presented, out _);
		var presentedHash = RefreshTokenHash.Create(RandomToken.Hash(Presented)).Value;
		_ = account.Rotate(presentedHash, RefreshTokenHash.Create(new string('c', 64)).Value, clock.GetUtcNow());
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Concurrency", "lost"), ResultExceptionType.Conflict)));
		_ = accounts.ReloadByRefreshTokenHashAsync(presentedHash, Arg.Any<CancellationToken>()).Returns<Task<UserAccount?>>(_ => throw new InvalidOperationException("secret detail of the database"));

		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		result.Error.Should().Be(SessionErrors.RefreshFailed);
		result.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
		logger.Lines.Should().Equal(
			"Warning: Refresh revocation not saved: refresh.revocation-unsaved (InvalidOperationException)",
			"Warning: Refresh refused: refresh.reused");
		logger.Lines.Should().NotContain(line => line.Contains("secret detail", StringComparison.Ordinal));
	}

	/// <summary>
	/// Verifies that a reuse whose account is gone when it is read again (erased meanwhile) is refused without a second save.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ReusedTokenAndTheAccountIsGoneOnReload_ShouldRefuseWithoutASecondSave()
	{
		var account = AccountHolding(Presented, out _);
		var presentedHash = RefreshTokenHash.Create(RandomToken.Hash(Presented)).Value;
		_ = account.Rotate(presentedHash, RefreshTokenHash.Create(new string('c', 64)).Value, clock.GetUtcNow());
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Concurrency", "lost"), ResultExceptionType.Conflict)));

		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		AssertRefused(result, RefreshFailureReason.TokenReused);
		await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a blank token and one longer than any refresh token are refused before the accounts are read.
	/// </summary>
	/// <param name="token">The token text.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	[InlineData("this-is-too-long-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
	public async Task Handle_BlankOrOversizedToken_ShouldRefuseWithoutReadingAccounts(string? token)
	{
		var result = await Handler().Handle(new RefreshSessionCommand(token!), CancellationToken.None);

		AssertRefused(result, RefreshFailureReason.TokenMalformed);
		await accounts.DidNotReceiveWithAnyArgs().FindByRefreshTokenHashAsync(default!, default);
	}

	/// <summary>
	/// Verifies that a token no account holds is refused and saves nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_UnknownToken_ShouldRefuseAndSaveNothing()
	{
		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		AssertRefused(result, RefreshFailureReason.TokenUnknown);
		await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
	}

	/// <summary>
	/// Verifies that a token of a revoked session is refused and saves nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_RevokedSession_ShouldRefuseAndSaveNothing()
	{
		var account = AccountHolding(Presented, out var first);
		_ = account.EndFamily(first.FamilyId);

		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		AssertRefused(result, RefreshFailureReason.SessionRevoked);
		await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
	}

	/// <summary>
	/// Verifies that a session unused for more than 30 days is refused and saves nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_SlidingExpiry_ShouldRefuseAndSaveNothing()
	{
		_ = AccountHolding(Presented, out _);
		clock.Advance(TimeSpan.FromDays(UserAccount.SlidingLifetimeDays + 1));

		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		AssertRefused(result, RefreshFailureReason.SessionExpired);
		await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
	}

	/// <summary>
	/// Verifies that losing the race for the same token (the save fails on the concurrency tokens) is a refusal and issues no token,
	/// so a token never gets two successors.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_SaveLosesTheRace_ShouldRefuseAndIssueNothing()
	{
		_ = AccountHolding(Presented, out _);
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Concurrency", "lost"), ResultExceptionType.Conflict)));

		var result = await Handler().Handle(new RefreshSessionCommand(Presented), CancellationToken.None);

		AssertRefused(result, RefreshFailureReason.SaveFailed);
		issuer.DidNotReceiveWithAnyArgs().Issue(default!, Guid.Empty, default);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Creates the handler on the replaced ports.
	/// </summary>
	/// <returns>The handler.</returns>
	private RefreshSessionCommandHandler Handler() => new(accounts, unitOfWork, issuer, clock, logger);

	/// <summary>
	/// Makes an account with one session that holds a token, and lets the repository find the account by that token.
	/// </summary>
	/// <param name="token">The clear refresh token of the session.</param>
	/// <param name="session">The session.</param>
	/// <returns>The account.</returns>
	private UserAccount AccountHolding(string token, out DeviceSession session)
	{
		var account = UserAccount.Create(SubjectHash.Create(new string('a', 64)).Value, clock.GetUtcNow());
		var hash = RefreshTokenHash.Create(RandomToken.Hash(token)).Value;
		session = account.StartSession(hash, clock.GetUtcNow());
		_ = accounts.FindByRefreshTokenHashAsync(hash, Arg.Any<CancellationToken>()).Returns(account);
		return account;
	}

	/// <summary>
	/// Checks the one generic refusal and that the log has the reason code and no token.
	/// </summary>
	/// <param name="result">The result.</param>
	/// <param name="reason">The reason code the log must carry.</param>
	private void AssertRefused(Result<RefreshSessionResponse> result, string reason)
	{
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SessionErrors.RefreshFailed);
		result.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
		logger.Lines.Should().ContainSingle().Which.Should().Be($"Warning: Refresh refused: {reason}").And.NotContain(Presented);
	}
	#endregion
}
