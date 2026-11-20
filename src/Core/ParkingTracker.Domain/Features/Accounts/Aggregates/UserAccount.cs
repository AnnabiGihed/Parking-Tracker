using ParkingTracker.Domain.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Primitives;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.Accounts.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The account of one signed-in user, known only by the keyed hash of the Google subject, with the refresh-token
///              sessions of the devices. A refresh token is used once; reusing a rotated one revokes its whole family. A family
///              lasts 90 days at most and ends after 30 days without a refresh. Every behaviour takes the current moment, so the
///              aggregate holds no clock; a revocation that must stay after a failure (the reuse) is on the aggregate for the
///              caller to save. It dispatches no domain event.
/// </summary>
public sealed class UserAccount : LightweightAggregateRoot<UserAccountId>
{
	#region Constants
	/// <summary>
	/// The days a family lasts from the sign-in, however often it refreshed.
	/// </summary>
	public const int AbsoluteLifetimeDays = 90;

	/// <summary>
	/// The days a session may stay unused before its family can no longer rotate.
	/// </summary>
	public const int SlidingLifetimeDays = 30;

	/// <summary>
	/// The most devices an account is signed in on at once; a sixth sign-in ends the oldest family.
	/// </summary>
	public const int MaximumActiveFamilies = 5;
	#endregion

	#region Fields
	/// <summary>
	/// The sessions of the account, every issued token once.
	/// </summary>
	private readonly List<DeviceSession> sessions = [];
	#endregion

	#region Properties
	/// <summary>
	/// Gets the keyed hash of the Google subject.
	/// </summary>
	public SubjectHash SubjectHash { get; private set; } = default!;

	/// <summary>
	/// Gets the day the account was created.
	/// </summary>
	public DateOnly CreatedOn { get; private set; }

	/// <summary>
	/// Gets the last day the user signed in or refreshed, to the day.
	/// </summary>
	public DateOnly LastSeenOn { get; private set; }

	/// <summary>
	/// Gets the sessions of the account, including rotated and revoked ones.
	/// </summary>
	public IReadOnlyCollection<DeviceSession> Sessions => sessions.AsReadOnly();
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="UserAccount"/> class for the persistence layer.
	/// </summary>
	private UserAccount()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="UserAccount"/> class.
	/// </summary>
	/// <param name="subjectHash">The keyed hash of the Google subject.</param>
	/// <param name="today">The day the account is created.</param>
	private UserAccount(SubjectHash subjectHash, DateOnly today)
		: base(UserAccountId.New())
	{
		SubjectHash = subjectHash;
		CreatedOn = today;
		LastSeenOn = today;
	}
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the account of a user who signs in for the first time.
	/// </summary>
	/// <param name="subjectHash">The keyed hash of the Google subject.</param>
	/// <param name="now">The current moment.</param>
	/// <returns>An account without sessions.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="subjectHash"/> is <see langword="null"/>.</exception>
	public static UserAccount Create(SubjectHash subjectHash, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(subjectHash);
		return new UserAccount(subjectHash, TodayOf(now.ToUniversalTime()));
	}
	#endregion

	#region Domain Behaviours
	/// <summary>
	/// Starts a session for a sign-in: a new family that lasts 90 days at most. When the account is signed in on five devices
	/// already, the family that started first is revoked.
	/// </summary>
	/// <param name="tokenHash">The hash of the refresh token handed out.</param>
	/// <param name="now">The current moment.</param>
	/// <returns>The new session.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="tokenHash"/> is <see langword="null"/>.</exception>
	public DeviceSession StartSession(RefreshTokenHash tokenHash, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(tokenHash);
		var moment = now.ToUniversalTime();
		RevokeOldestFamilyBeyondTheCap(moment);
		var session = DeviceSession.Issue(Guid.NewGuid(), tokenHash, TodayOf(moment), moment.AddDays(AbsoluteLifetimeDays));
		sessions.Add(session);
		LastSeenOn = TodayOf(moment);
		Version++;
		return session;
	}

	/// <summary>
	/// Exchanges a refresh token for the next one of its family. Presenting a token that was exchanged before revokes the whole
	/// family, and that revocation stays on the account for the caller to save.
	/// </summary>
	/// <param name="presentedHash">The hash of the refresh token the client presented.</param>
	/// <param name="newHash">The hash of the refresh token handed out instead.</param>
	/// <param name="now">The current moment.</param>
	/// <returns>The new session, or a failure when the token is unknown, revoked, reused or expired.</returns>
	/// <exception cref="ArgumentNullException">A hash is <see langword="null"/>.</exception>
	public Result<DeviceSession> Rotate(RefreshTokenHash presentedHash, RefreshTokenHash newHash, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(presentedHash);
		ArgumentNullException.ThrowIfNull(newHash);
		var moment = now.ToUniversalTime();
		var presented = FindByToken(presentedHash);
		if (presented is null)
		{
			return Result.Failure<DeviceSession>(AccountErrors.SessionNotFound, ResultExceptionType.NotFound);
		}

		if (presented.Revoked)
		{
			return Result.Failure<DeviceSession>(AccountErrors.SessionRevoked, ResultExceptionType.AuthenticationRequired);
		}

		if (presented.RotatedOn is not null)
		{
			RevokeFamily(presented.FamilyId);
			Version++;
			return Result.Failure<DeviceSession>(AccountErrors.RefreshTokenReused, ResultExceptionType.AuthenticationRequired);
		}

		if (presented.IsExpiredAt(moment, SlidingLifetimeDays))
		{
			return Result.Failure<DeviceSession>(AccountErrors.SessionExpired, ResultExceptionType.AuthenticationRequired);
		}

		presented.MarkRotated(moment);
		var next = DeviceSession.Issue(presented.FamilyId, newHash, TodayOf(moment), presented.AbsoluteExpiryUtc);
		sessions.Add(next);
		LastSeenOn = TodayOf(moment);
		Version++;
		return next;
	}

	/// <summary>
	/// Signs out the device of a refresh token: revokes every session of its family.
	/// </summary>
	/// <param name="tokenHash">The hash of a refresh token of the family.</param>
	/// <returns>Success, or a not-found failure when the account has no session with the token.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="tokenHash"/> is <see langword="null"/>.</exception>
	public Result EndFamilyOf(RefreshTokenHash tokenHash)
	{
		ArgumentNullException.ThrowIfNull(tokenHash);
		return EndFamilyOf(FindByToken(tokenHash));
	}

	/// <summary>
	/// Signs out the device of a session: revokes every session of its family.
	/// </summary>
	/// <param name="sessionId">The identifier of a session of the family.</param>
	/// <returns>Success, or a not-found failure when the account has no such session.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="sessionId"/> is <see langword="null"/>.</exception>
	public Result EndFamilyOf(DeviceSessionId sessionId)
	{
		ArgumentNullException.ThrowIfNull(sessionId);
		return EndFamilyOf(sessions.Find(session => session.Id == sessionId));
	}

	/// <summary>
	/// Signs out a device by its family: revokes every session of the family, the one an access token names in its session claim.
	/// </summary>
	/// <param name="familyId">The family of the device.</param>
	/// <returns>Success, or a not-found failure when the account has no session of the family.</returns>
	public Result EndFamily(Guid familyId)
	{
		return EndFamilyOf(sessions.Find(session => session.FamilyId == familyId));
	}

	/// <summary>
	/// Counts the signed-in devices: the families that still hold a token that is unused, not revoked and not expired.
	/// </summary>
	/// <param name="now">The current moment.</param>
	/// <returns>The number of active families.</returns>
	public int ActiveSessionCount(DateTimeOffset now)
	{
		return ActiveSessions(now.ToUniversalTime()).Select(session => session.FamilyId).Distinct().Count();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Revokes the family of a session, or tells that there is none.
	/// </summary>
	/// <param name="session">The session whose family ends; <see langword="null"/> when none was found.</param>
	/// <returns>Success, or a not-found failure when <paramref name="session"/> is <see langword="null"/>.</returns>
	private Result EndFamilyOf(DeviceSession? session)
	{
		if (session is null)
		{
			return Result.Failure(AccountErrors.SessionNotFound, ResultExceptionType.NotFound);
		}

		RevokeFamily(session.FamilyId);
		Version++;
		return Result.Success();
	}

	/// <summary>
	/// Revokes every session of a family.
	/// </summary>
	/// <param name="familyId">The family to revoke.</param>
	private void RevokeFamily(Guid familyId)
	{
		foreach (var member in sessions.Where(session => session.FamilyId == familyId))
		{
			member.Revoke();
		}
	}

	/// <summary>
	/// Revokes the family that started first when the account is signed in on the most devices it may be.
	/// </summary>
	/// <param name="moment">The current moment, in UTC.</param>
	private void RevokeOldestFamilyBeyondTheCap(DateTimeOffset moment)
	{
		var active = ActiveSessions(moment).ToList();
		if (active.Select(session => session.FamilyId).Distinct().Count() >= MaximumActiveFamilies)
		{
			RevokeFamily(active.MinBy(session => session.AbsoluteExpiryUtc)!.FamilyId);
		}
	}

	/// <summary>
	/// Lists the sessions that are the live token of their family: unused, not revoked and not expired.
	/// </summary>
	/// <param name="moment">The moment to judge at, in UTC.</param>
	/// <returns>The live sessions.</returns>
	private IEnumerable<DeviceSession> ActiveSessions(DateTimeOffset moment)
	{
		return sessions.Where(session => !session.Revoked && session.RotatedOn is null && !session.IsExpiredAt(moment, SlidingLifetimeDays));
	}

	/// <summary>
	/// Finds the session that holds a refresh token.
	/// </summary>
	/// <param name="tokenHash">The hash of the refresh token.</param>
	/// <returns>The session, or <see langword="null"/> when the account has none.</returns>
	private DeviceSession? FindByToken(RefreshTokenHash tokenHash)
	{
		return sessions.Find(session => session.TokenHash == tokenHash);
	}

	/// <summary>
	/// Reads the UTC day of a moment, the precision the account keeps its dates in.
	/// </summary>
	/// <param name="now">The moment.</param>
	/// <returns>The UTC day.</returns>
	private static DateOnly TodayOf(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);
	#endregion
}
