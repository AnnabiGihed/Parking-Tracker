using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.CreateSignInChallenge;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Backend.Application.Features.Accounts.Queries.GetMe;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="CreateSignInChallengeCommandHandler"/> and <see cref="GetMeQueryHandler"/> with the ports
///              replaced: the nonce and its five minutes, the purge limit, and the account read with its active session count, an
///              unknown or empty identifier being an authentication failure.
/// </summary>
public sealed class CreateAndGetMeHandlerTests
{
	#region Fields
	/// <summary>
	/// The clock.
	/// </summary>
	private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
	#endregion

	#region Challenge Tests
	/// <summary>
	/// Verifies that the challenge stores a random nonce for five minutes, purges at the current moment and returns what it stored.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CreateChallenge_ShouldStoreARandomNonceForFiveMinutesAndReturnIt()
	{
		var store = Substitute.For<ISignInChallengeStore>();
		var handler = new CreateSignInChallengeCommandHandler(store, clock);

		var result = await handler.Handle(new CreateSignInChallengeCommand(), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.Nonce.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
		result.Value.ExpiresAt.Should().Be(clock.GetUtcNow().AddMinutes(5));
		await store.Received(1).CreateAsync(result.Value.Nonce, result.Value.ExpiresAt, clock.GetUtcNow(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that two challenges get different nonces.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task CreateChallenge_Twice_ShouldGiveDifferentNonces()
	{
		var handler = new CreateSignInChallengeCommandHandler(Substitute.For<ISignInChallengeStore>(), clock);

		var first = await handler.Handle(new CreateSignInChallengeCommand(), CancellationToken.None);
		var second = await handler.Handle(new CreateSignInChallengeCommand(), CancellationToken.None);

		first.Value.Nonce.Should().NotBe(second.Value.Nonce);
	}
	#endregion

	#region Me Tests
	/// <summary>
	/// Verifies that the account is read with its dates and the number of devices still signed in.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task GetMe_KnownAccount_ShouldReturnItsDatesAndActiveSessions()
	{
		var account = UserAccount.Create(SubjectHash.Create(new string('b', 64)).Value, clock.GetUtcNow());
		_ = account.StartSession(RefreshTokenHash.Create(new string('1', 64)).Value, clock.GetUtcNow());
		_ = account.StartSession(RefreshTokenHash.Create(new string('2', 64)).Value, clock.GetUtcNow());
		var accounts = Substitute.For<IUserAccountRepository>();
		_ = accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
		var handler = new GetMeQueryHandler(accounts, clock);

		var result = await handler.Handle(new GetMeQuery(account.Id.Value), CancellationToken.None);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().Be(new GetMeResponse(account.Id.Value, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), 2));
	}

	/// <summary>
	/// Verifies that an account that does not exist, and the empty identifier, are authentication failures, so the old access token
	/// of an erased account stops working.
	/// </summary>
	/// <param name="empty">Whether the identifier is the empty one.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task GetMe_UnknownAccount_ShouldFailAsAuthenticationRequired(bool empty)
	{
		var accounts = Substitute.For<IUserAccountRepository>();
		var handler = new GetMeQueryHandler(accounts, clock);

		var result = await handler.Handle(new GetMeQuery(empty ? Guid.Empty : Guid.NewGuid()), CancellationToken.None);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SessionErrors.AccountUnknown);
		result.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
	}
	#endregion
}
