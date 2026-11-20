using Microsoft.Extensions.Logging;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Backend.Application.Features.Accounts.Services;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Commands;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.ExchangeGoogleIdToken;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="ExchangeGoogleIdTokenCommand"/> (ADR-0014): validates the Google ID token, consumes the nonce in
///              it with one atomic statement, hashes the subject, finds or creates the account, starts a session with a new refresh
///              token (kept as its SHA-256 hash only), saves, and issues the access token. The nonce is consumed before anything
///              else is written and outside the unit of work, so it stays consumed when a later step fails. Every failure is
///              the same <see cref="SessionErrors.SignInFailed"/>, answered as an authentication failure; the cause is logged as a
///              reason code and never with the token, the subject or the nonce.
/// </summary>
public sealed partial class ExchangeGoogleIdTokenCommandHandler : ICommandHandler<ExchangeGoogleIdTokenCommand, ExchangeGoogleIdTokenResponse>
{
	#region Constants
	/// <summary>
	/// The most characters a Google ID token may have; a real one is under 2000, so a longer text is refused unread.
	/// </summary>
	public const int MaximumTokenLength = 8192;
	#endregion

	#region Fields
	/// <summary>
	/// Validates the ID token.
	/// </summary>
	private readonly IGoogleIdTokenValidator validator;

	/// <summary>
	/// The store of the nonces.
	/// </summary>
	private readonly ISignInChallengeStore challenges;

	/// <summary>
	/// Hashes the Google subject.
	/// </summary>
	private readonly ISubjectHasher subjectHasher;

	/// <summary>
	/// The accounts.
	/// </summary>
	private readonly IUserAccountRepository accounts;

	/// <summary>
	/// Saves the account and its session.
	/// </summary>
	private readonly IUnitOfWork unitOfWork;

	/// <summary>
	/// Issues the access token.
	/// </summary>
	private readonly IAccessTokenIssuer tokenIssuer;

	/// <summary>
	/// The clock.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<ExchangeGoogleIdTokenCommandHandler> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ExchangeGoogleIdTokenCommandHandler"/> class.
	/// </summary>
	/// <param name="validator">Validates the ID token.</param>
	/// <param name="challenges">The store of the nonces.</param>
	/// <param name="subjectHasher">Hashes the Google subject.</param>
	/// <param name="accounts">The accounts.</param>
	/// <param name="unitOfWork">Saves the account and its session.</param>
	/// <param name="tokenIssuer">Issues the access token.</param>
	/// <param name="clock">The clock.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ExchangeGoogleIdTokenCommandHandler(
		IGoogleIdTokenValidator validator,
		ISignInChallengeStore challenges,
		ISubjectHasher subjectHasher,
		IUserAccountRepository accounts,
		IUnitOfWork unitOfWork,
		IAccessTokenIssuer tokenIssuer,
		TimeProvider clock,
		ILogger<ExchangeGoogleIdTokenCommandHandler> logger)
	{
		ArgumentNullException.ThrowIfNull(validator);
		ArgumentNullException.ThrowIfNull(challenges);
		ArgumentNullException.ThrowIfNull(subjectHasher);
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(tokenIssuer);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		this.validator = validator;
		this.challenges = challenges;
		this.subjectHasher = subjectHasher;
		this.accounts = accounts;
		this.unitOfWork = unitOfWork;
		this.tokenIssuer = tokenIssuer;
		this.clock = clock;
		this.logger = logger;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<ExchangeGoogleIdTokenResponse>> Handle(ExchangeGoogleIdTokenCommand request, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.IdToken) || request.IdToken.Length > MaximumTokenLength)
		{
			return Refuse(SignInFailureReason.TokenMissing);
		}

		var identity = await validator.ValidateAsync(request.IdToken, cancellationToken).ConfigureAwait(false);
		if (identity.IsFailure)
		{
			return Refuse(identity.Error.Code);
		}

		var now = clock.GetUtcNow();
		if (!await challenges.ConsumeAsync(identity.Value.Nonce, now, cancellationToken).ConfigureAwait(false))
		{
			return Refuse(SignInFailureReason.NonceRejected);
		}

		return await SignInAsync(identity.Value.Subject, now, cancellationToken).ConfigureAwait(false);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Finds or creates the account of the subject, starts the session, saves and issues the tokens.
	/// </summary>
	/// <param name="subject">The Google subject.</param>
	/// <param name="now">The current moment.</param>
	/// <param name="cancellationToken">Cancels the work.</param>
	/// <returns>The tokens, or the generic failure.</returns>
	private async Task<Result<ExchangeGoogleIdTokenResponse>> SignInAsync(string subject, DateTimeOffset now, CancellationToken cancellationToken)
	{
		var subjectHash = SubjectHash.Create(subjectHasher.Hash(subject));
		var refreshToken = RandomToken.New();
		var tokenHash = RefreshTokenHash.Create(RandomToken.Hash(refreshToken));
		if (subjectHash.IsFailure || tokenHash.IsFailure)
		{
			return Refuse(SignInFailureReason.HashInvalid);
		}

		var account = await StartSessionAsync(subjectHash.Value, tokenHash.Value, now, cancellationToken).ConfigureAwait(false);
		if (account is null)
		{
			return Refuse(SignInFailureReason.SaveFailed);
		}

		var accessToken = tokenIssuer.Issue(account.Id, now);
		return new ExchangeGoogleIdTokenResponse(accessToken.Token, accessToken.ExpiresAt, refreshToken);
	}

	/// <summary>
	/// Starts and saves a session on the account of the subject, creating the account when there is none. Two first sign-ins of one
	/// subject can race: the unique index lets one create the account and refuses the other. The loser discards its staged account,
	/// reads the winner and starts its session there, once; any other save failure is a failure.
	/// </summary>
	/// <param name="subjectHash">The hash of the Google subject.</param>
	/// <param name="tokenHash">The hash of the new refresh token.</param>
	/// <param name="now">The current moment.</param>
	/// <param name="cancellationToken">Cancels the work.</param>
	/// <returns>The account that holds the new session, or <see langword="null"/> when it could not be saved.</returns>
	private async Task<UserAccount?> StartSessionAsync(SubjectHash subjectHash, RefreshTokenHash tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
	{
		var existing = await accounts.FindBySubjectHashAsync(subjectHash, cancellationToken).ConfigureAwait(false);
		if (existing is not null)
		{
			return await SaveSessionAsync(existing, tokenHash, now, cancellationToken).ConfigureAwait(false);
		}

		var created = UserAccount.Create(subjectHash, now);
		await accounts.AddAsync(created, cancellationToken).ConfigureAwait(false);
		var saved = await SaveSessionAsync(created, tokenHash, now, cancellationToken).ConfigureAwait(false);
		if (saved is not null)
		{
			return saved;
		}

		await accounts.DeleteAsync(created, cancellationToken).ConfigureAwait(false);
		var winner = await accounts.FindBySubjectHashAsync(subjectHash, cancellationToken).ConfigureAwait(false);
		return winner is null ? null : await SaveSessionAsync(winner, tokenHash, now, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Starts a session on an account and saves.
	/// </summary>
	/// <param name="account">The account.</param>
	/// <param name="tokenHash">The hash of the new refresh token.</param>
	/// <param name="now">The current moment.</param>
	/// <param name="cancellationToken">Cancels the save.</param>
	/// <returns>The account, or <see langword="null"/> when the save failed.</returns>
	private async Task<UserAccount?> SaveSessionAsync(UserAccount account, RefreshTokenHash tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
	{
		_ = account.StartSession(tokenHash, now);
		var save = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return save.IsSuccess ? account : null;
	}

	/// <summary>
	/// Logs the reason code and returns the one failure every refusal shares.
	/// </summary>
	/// <param name="reason">The reason code; never the token, the subject or the nonce.</param>
	/// <returns>The generic authentication failure.</returns>
	private Result<ExchangeGoogleIdTokenResponse> Refuse(string reason)
	{
		LogRefused(logger, reason);
		return Result.Failure<ExchangeGoogleIdTokenResponse>(SessionErrors.SignInFailed, ResultExceptionType.AuthenticationRequired);
	}

	/// <summary>
	/// Writes the log line of a refused sign-in.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="reason">The reason code.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in refused: {Reason}")]
	private static partial void LogRefused(ILogger logger, string reason);
	#endregion
}
