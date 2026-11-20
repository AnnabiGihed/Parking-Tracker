using Microsoft.Extensions.Logging;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Errors;
using ParkingTracker.Backend.Application.Features.Accounts.Services;
using ParkingTracker.Domain.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Commands;
using Pivot.Framework.Domain.Repositories;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.RefreshSession;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="RefreshSessionCommand"/> (ADR-0014, ADR-0015): hashes the token, finds the account that holds it
///              and lets the account rotate it. A rotation is saved and answered with a new access token and the next refresh token.
///              The reuse of a rotated token revokes its whole family in the domain; that revocation is saved before the refusal, so
///              it stays. Two requests that present the same token race on the concurrency tokens of the session and the account:
///              the loser fails to save and is refused, so a token never has two successors. Every refusal is the same
///              <see cref="SessionErrors.RefreshFailed"/>, answered as an authentication failure; the cause is logged as a reason
///              code and never with the token.
/// </summary>
public sealed partial class RefreshSessionCommandHandler : ICommandHandler<RefreshSessionCommand, RefreshSessionResponse>
{
	#region Constants
	/// <summary>
	/// The most characters a refresh token may have; a real one has 43, so a longer text is refused unread.
	/// </summary>
	public const int MaximumTokenLength = 256;
	#endregion

	#region Fields
	/// <summary>
	/// The accounts.
	/// </summary>
	private readonly IUserAccountRepository accounts;

	/// <summary>
	/// Saves the rotation or the revocation.
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
	private readonly ILogger<RefreshSessionCommandHandler> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="RefreshSessionCommandHandler"/> class.
	/// </summary>
	/// <param name="accounts">The accounts.</param>
	/// <param name="unitOfWork">Saves the rotation or the revocation.</param>
	/// <param name="tokenIssuer">Issues the access token.</param>
	/// <param name="clock">The clock.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public RefreshSessionCommandHandler(
		IUserAccountRepository accounts,
		IUnitOfWork unitOfWork,
		IAccessTokenIssuer tokenIssuer,
		TimeProvider clock,
		ILogger<RefreshSessionCommandHandler> logger)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(tokenIssuer);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		this.accounts = accounts;
		this.unitOfWork = unitOfWork;
		this.tokenIssuer = tokenIssuer;
		this.clock = clock;
		this.logger = logger;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<RefreshSessionResponse>> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > MaximumTokenLength)
		{
			return Refuse(RefreshFailureReason.TokenMalformed);
		}

		var presented = RefreshTokenHash.Create(RandomToken.Hash(request.RefreshToken));
		var next = RandomToken.New();
		var nextHash = RefreshTokenHash.Create(RandomToken.Hash(next));
		if (presented.IsFailure || nextHash.IsFailure)
		{
			return Refuse(RefreshFailureReason.TokenMalformed);
		}

		var account = await accounts.FindByRefreshTokenHashAsync(presented.Value, cancellationToken).ConfigureAwait(false);
		if (account is null)
		{
			return Refuse(RefreshFailureReason.TokenUnknown);
		}

		var now = clock.GetUtcNow();
		var rotation = account.Rotate(presented.Value, nextHash.Value, now);
		if (rotation.IsFailure)
		{
			return await RefuseRotationAsync(rotation.Error, presented.Value, cancellationToken).ConfigureAwait(false);
		}

		var save = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		if (save.IsFailure)
		{
			return Refuse(RefreshFailureReason.SaveFailed);
		}

		var accessToken = tokenIssuer.Issue(account.Id, rotation.Value.FamilyId, now);
		return new RefreshSessionResponse(accessToken.Token, accessToken.ExpiresAt, next);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Refuses a rotation the account declined. A reuse has revoked the family on the account, and that is saved first: the
	/// refusal must not undo it. A failure to save it does not change the answer.
	/// </summary>
	/// <param name="error">The error the account gave.</param>
	/// <param name="presented">The hash of the token that was presented.</param>
	/// <param name="cancellationToken">Cancels the save.</param>
	/// <returns>The generic authentication failure.</returns>
	private async Task<Result<RefreshSessionResponse>> RefuseRotationAsync(Error error, RefreshTokenHash presented, CancellationToken cancellationToken)
	{
		if (error == AccountErrors.RefreshTokenReused)
		{
			await SaveRevocationAsync(presented, cancellationToken).ConfigureAwait(false);
			return Refuse(RefreshFailureReason.TokenReused);
		}

		if (error == AccountErrors.SessionRevoked)
		{
			return Refuse(RefreshFailureReason.SessionRevoked);
		}

		// An error the account does not give today (a new domain failure) is refused like an unknown token: the caller learns nothing
		// either way, and the reason code in the log is where a developer sees that a new case needs a code of its own.
		return Refuse(error == AccountErrors.SessionExpired ? RefreshFailureReason.SessionExpired : RefreshFailureReason.TokenUnknown);
	}

	/// <summary>
	/// Saves the revocation of a family after a reuse. When the save fails (a second request changed the account first), the account
	/// is read again and its family is revoked once more; when that fails as well, the failure is logged, because a family that
	/// stays usable after a reuse is something the operator must see.
	/// </summary>
	/// <param name="presented">The hash of the token that was reused.</param>
	/// <param name="cancellationToken">Cancels the saves.</param>
	/// <returns>A task that completes when the revocation was saved or given up.</returns>
	private async Task SaveRevocationAsync(RefreshTokenHash presented, CancellationToken cancellationToken)
	{
		try
		{
			if (!await TryRevokeOnceMoreAsync(presented, cancellationToken).ConfigureAwait(false))
			{
				LogRevocationUnsaved(logger, RefreshFailureReason.RevocationUnsaved);
			}
		}
		catch (Exception problem) when (problem is not OperationCanceledException)
		{
			// A database error while reading again must not turn the refusal into a server error: the caller still gets the generic
			// refusal, and only the type of the exception is logged, never its message, which can hold data.
			LogRevocationFailed(logger, RefreshFailureReason.RevocationUnsaved, problem.GetType().Name);
		}
	}

	/// <summary>
	/// Saves the revocation staged on the account; when that fails, reads the account again and revokes its family once more.
	/// </summary>
	/// <param name="presented">The hash of the token that was reused.</param>
	/// <param name="cancellationToken">Cancels the saves.</param>
	/// <returns><see langword="true"/> when the revocation is saved, or no longer needed because the account or the token is gone.</returns>
	private async Task<bool> TryRevokeOnceMoreAsync(RefreshTokenHash presented, CancellationToken cancellationToken)
	{
		if ((await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false)).IsSuccess)
		{
			return true;
		}

		var reloaded = await accounts.ReloadByRefreshTokenHashAsync(presented, cancellationToken).ConfigureAwait(false);
		var family = reloaded?.Sessions.FirstOrDefault(session => session.TokenHash == presented)?.FamilyId;
		if (reloaded is null || family is null)
		{
			return true;
		}

		_ = reloaded.EndFamily(family.Value);
		return (await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false)).IsSuccess;
	}

	/// <summary>
	/// Logs the reason code and returns the one failure every refusal shares.
	/// </summary>
	/// <param name="reason">The reason code; never the token.</param>
	/// <returns>The generic authentication failure.</returns>
	private Result<RefreshSessionResponse> Refuse(string reason)
	{
		LogRefused(logger, reason);
		return Result.Failure<RefreshSessionResponse>(SessionErrors.RefreshFailed, ResultExceptionType.AuthenticationRequired);
	}

	/// <summary>
	/// Writes the log line of a refused refresh.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="reason">The reason code.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Refresh refused: {Reason}")]
	private static partial void LogRefused(ILogger logger, string reason);

	/// <summary>
	/// Writes the log line of a revocation that could not be saved.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="reason">The reason code.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Refresh revocation not saved: {Reason}")]
	private static partial void LogRevocationUnsaved(ILogger logger, string reason);

	/// <summary>
	/// Writes the log line of a revocation that failed with an exception.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="reason">The reason code.</param>
	/// <param name="exceptionType">The type name of the exception, without its message.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "Refresh revocation not saved: {Reason} ({ExceptionType})")]
	private static partial void LogRevocationFailed(ILogger logger, string reason, string exceptionType);
	#endregion
}
