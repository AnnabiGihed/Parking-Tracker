using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.ExchangeGoogleIdToken;
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
/// Purpose     : Unit tests for <see cref="ExchangeGoogleIdTokenCommandHandler"/> with the ports replaced: the order of the steps (the
///              nonce is consumed after the token is validated and before anything is saved), what is stored (hashes only), the
///              reuse of an account, and that every refusal is the same authentication failure that logs a reason code and never the
///              token, the subject or the nonce.
/// </summary>
public sealed class ExchangeGoogleIdTokenCommandHandlerTests
{
	#region Constants
	/// <summary>
	/// The token the tests send.
	/// </summary>
	private const string IdToken = "eyJ.the-id-token.signature";

	/// <summary>
	/// The Google subject of the token.
	/// </summary>
	private const string Subject = "google-subject-42";

	/// <summary>
	/// The nonce of the token.
	/// </summary>
	private const string Nonce = "the-nonce-of-the-token";

	/// <summary>
	/// The subject hash the hasher answers.
	/// </summary>
	private static readonly string SubjectHashText = new('a', 64);
	#endregion

	#region Fields
	/// <summary>
	/// The validator.
	/// </summary>
	private readonly IGoogleIdTokenValidator validator = Substitute.For<IGoogleIdTokenValidator>();

	/// <summary>
	/// The store of the nonces.
	/// </summary>
	private readonly ISignInChallengeStore challenges = Substitute.For<ISignInChallengeStore>();

	/// <summary>
	/// The subject hasher.
	/// </summary>
	private readonly ISubjectHasher hasher = Substitute.For<ISubjectHasher>();

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
	private readonly ListLogger<ExchangeGoogleIdTokenCommandHandler> logger = new();
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ExchangeGoogleIdTokenCommandHandlerTests"/> class with ports that let a sign-in succeed.
	/// </summary>
	public ExchangeGoogleIdTokenCommandHandlerTests()
	{
		_ = validator.ValidateAsync(IdToken, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(new GoogleIdentity(Subject, Nonce))));
		_ = challenges.ConsumeAsync(Nonce, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
		_ = hasher.Hash(Subject).Returns(SubjectHashText);
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success()));
		_ = issuer.Issue(Arg.Any<UserAccountId>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()).Returns(call => new IssuedAccessToken("the-access-token", call.Arg<DateTimeOffset>().AddMinutes(15)));
	}
	#endregion

	#region Success Tests
	/// <summary>
	/// Verifies that a first sign-in adds an account with one session, stores only the hash of the refresh token, saves once and
	/// returns the tokens.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_NewSubject_ShouldAddTheAccountWithOneSessionAndReturnTheTokens()
	{
		UserAccount? added = null;
		await accounts.AddAsync(Arg.Do<UserAccount>(account => added = account), Arg.Any<CancellationToken>());

		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(IdToken), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.AccessToken.Should().Be("the-access-token");
		result.Value.AccessTokenExpiresAt.Should().Be(clock.GetUtcNow().AddMinutes(15));
		result.Value.RefreshToken.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
		added!.SubjectHash.Value.Should().Be(SubjectHashText);
		added.Sessions.Should().ContainSingle().Which.TokenHash.Value.Should().Be(RandomToken.Hash(result.Value.RefreshToken));
		await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
		logger.Lines.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a known subject gets its account back with a second session and no second account.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_KnownSubject_ShouldStartAnotherSessionOnTheSameAccount()
	{
		var existing = UserAccount.Create(SubjectHash.Create(SubjectHashText).Value, clock.GetUtcNow());
		_ = accounts.FindBySubjectHashAsync(Arg.Is<SubjectHash>(hash => hash.Value == SubjectHashText), Arg.Any<CancellationToken>()).Returns(existing);

		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(IdToken), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		existing.Sessions.Should().ContainSingle();
		await accounts.DidNotReceive().AddAsync(Arg.Any<UserAccount>(), Arg.Any<CancellationToken>());
		issuer.Received(1).Issue(existing.Id, existing.Sessions.Single().FamilyId, Arg.Any<DateTimeOffset>());
	}

	/// <summary>
	/// Verifies the race of two first sign-ins of one subject: when the save of the new account is refused, the handler discards
	/// the staged account, reads the account that won, starts its session there, saves once more and succeeds.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_FirstSignInLosesTheRace_ShouldStartTheSessionOnTheWinnersAccount()
	{
		var winner = UserAccount.Create(SubjectHash.Create(SubjectHashText).Value, clock.GetUtcNow());
		_ = accounts.FindBySubjectHashAsync(Arg.Any<SubjectHash>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<UserAccount?>(null), Task.FromResult<UserAccount?>(winner));
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Failure(new Error("Persistence.Database", "unique"), ResultExceptionType.Conflict)), Task.FromResult(Result.Success()));

		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(IdToken), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		winner.Sessions.Should().ContainSingle();
		await accounts.Received(1).DeleteAsync(Arg.Any<UserAccount>(), Arg.Any<CancellationToken>());
		await unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
		issuer.Received(1).Issue(winner.Id, winner.Sessions.Single().FamilyId, Arg.Any<DateTimeOffset>());
	}

	/// <summary>
	/// Verifies that the second chance is taken once: when the retry on the winner fails as well, the handler gives the generic
	/// failure and does not loop.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_RetryOnTheWinnerFails_ShouldFailGenericallyAfterTwoSaves()
	{
		var winner = UserAccount.Create(SubjectHash.Create(SubjectHashText).Value, clock.GetUtcNow());
		_ = accounts.FindBySubjectHashAsync(Arg.Any<SubjectHash>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<UserAccount?>(null), Task.FromResult<UserAccount?>(winner));
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Failure(new Error("Persistence.Database", "refused"), ResultExceptionType.Conflict)));

		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(IdToken), CancellationToken.None);

		AssertGenericFailure(result, SignInFailureReason.SaveFailed);
		await unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that the nonce is consumed after the validation and before the account is read or saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ShouldConsumeTheNonceAfterValidationAndBeforeTheSave()
	{
		_ = await Handler().Handle(new ExchangeGoogleIdTokenCommand(IdToken), CancellationToken.None);

		Received.InOrder(() =>
		{
			_ = validator.ValidateAsync(IdToken, Arg.Any<CancellationToken>());
			_ = challenges.ConsumeAsync(Nonce, clock.GetUtcNow(), Arg.Any<CancellationToken>());
			_ = accounts.FindBySubjectHashAsync(Arg.Any<SubjectHash>(), Arg.Any<CancellationToken>());
			_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
		});
	}
	#endregion

	#region Refusal Tests
	/// <summary>
	/// Verifies that a token the validator refuses gives the generic failure, consumes no nonce, touches no account and logs the
	/// reason code of the validator.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_ValidatorRefuses_ShouldFailGenericallyWithoutConsumingTheNonce()
	{
		_ = validator.ValidateAsync(IdToken, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure<GoogleIdentity>(new Error("token.audience", "refused"), ResultExceptionType.AuthenticationRequired)));

		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(IdToken), CancellationToken.None);

		AssertGenericFailure(result, "token.audience");
		await challenges.DidNotReceiveWithAnyArgs().ConsumeAsync(default!, default, default);
		await accounts.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
		await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
	}

	/// <summary>
	/// Verifies that a nonce that is unknown, used or expired gives the generic failure and saves nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_NonceRejected_ShouldFailGenericallyAndSaveNothing()
	{
		_ = challenges.ConsumeAsync(Nonce, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(IdToken), CancellationToken.None);

		AssertGenericFailure(result, SignInFailureReason.NonceRejected);
		await accounts.DidNotReceiveWithAnyArgs().FindBySubjectHashAsync(default!, default);
		await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
		issuer.DidNotReceiveWithAnyArgs().Issue(default!, Guid.Empty, default);
	}

	/// <summary>
	/// Verifies that a failed save gives the generic failure, issues no token, and happens after the nonce was consumed.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_SaveFails_ShouldFailGenericallyAfterTheNonceWasConsumed()
	{
		_ = unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Failure(new Error("Persistence.Database", "refused"), ResultExceptionType.Conflict)));

		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(IdToken), CancellationToken.None);

		AssertGenericFailure(result, SignInFailureReason.SaveFailed);
		await accounts.Received(1).DeleteAsync(Arg.Any<UserAccount>(), Arg.Any<CancellationToken>());
		await challenges.Received(1).ConsumeAsync(Nonce, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
		issuer.DidNotReceiveWithAnyArgs().Issue(default!, Guid.Empty, default);
	}

	/// <summary>
	/// Verifies that a hasher that returns something that is not a hash gives the generic failure, and nothing is saved.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_HasherReturnsAMalformedHash_ShouldFailGenerically()
	{
		_ = hasher.Hash(Subject).Returns("not-a-hash");

		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(IdToken), CancellationToken.None);

		AssertGenericFailure(result, SignInFailureReason.HashInvalid);
		await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
	}

	/// <summary>
	/// Verifies that a blank token and a token longer than any ID token are refused without calling the validator.
	/// </summary>
	/// <param name="token">The token text.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	public async Task Handle_BlankToken_ShouldFailGenericallyWithoutValidating(string? token)
	{
		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(token!), CancellationToken.None);

		AssertGenericFailure(result, SignInFailureReason.TokenMissing);
		await validator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default);
	}

	/// <summary>
	/// Verifies that a token over the maximum length is refused unread.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Handle_OversizedToken_ShouldFailGenericallyWithoutValidating()
	{
		var result = await Handler().Handle(new ExchangeGoogleIdTokenCommand(new string('a', ExchangeGoogleIdTokenCommandHandler.MaximumTokenLength + 1)), CancellationToken.None);

		AssertGenericFailure(result, SignInFailureReason.TokenMissing);
		await validator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default);
	}
	#endregion

	#region Constructor Tests
	/// <summary>
	/// Verifies that a missing dependency is rejected when the handler is built.
	/// </summary>
	[Fact]
	public void Constructor_NullDependency_ShouldThrow()
	{
		var act = () => new ExchangeGoogleIdTokenCommandHandler(validator, challenges, hasher, accounts, unitOfWork, issuer, clock, null!); // null on purpose: proves the guard clause rejects it

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the handler over the substitutes.
	/// </summary>
	/// <returns>The handler.</returns>
	private ExchangeGoogleIdTokenCommandHandler Handler()
	{
		return new ExchangeGoogleIdTokenCommandHandler(validator, challenges, hasher, accounts, unitOfWork, issuer, clock, logger);
	}

	/// <summary>
	/// Checks that a result is the one generic authentication failure, and that the log holds the reason code and no secret.
	/// </summary>
	/// <param name="result">The result.</param>
	/// <param name="reason">The reason code expected in the log.</param>
	private void AssertGenericFailure(Result<ExchangeGoogleIdTokenResponse> result, string reason)
	{
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SessionErrors.SignInFailed);
		result.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
		logger.Lines.Should().ContainSingle().Which.Should().Be($"Warning: Sign-in refused: {reason}");
		logger.Lines.Should().NotContain(line => line.Contains(IdToken, StringComparison.Ordinal) || line.Contains(Subject, StringComparison.Ordinal) || line.Contains(Nonce, StringComparison.Ordinal));
	}
	#endregion
}
