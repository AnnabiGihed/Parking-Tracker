using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Primitives;

namespace ParkingTracker.Domain.Features.Accounts.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One issued refresh token of an account. A token is used once: rotating it sets <see cref="RotatedOn"/> and issues a
///              new session in the same family, so presenting the old token again is a reuse. It is a child of
///              <see cref="UserAccount"/>, which decides when a session may rotate, expire or be revoked.
/// </summary>
public sealed class DeviceSession : Entity<DeviceSessionId>
{
	#region Properties
	/// <summary>
	/// Gets the family of the session: every token issued by rotating the first one of a sign-in shares it.
	/// </summary>
	public Guid FamilyId { get; private set; }

	/// <summary>
	/// Gets the hash of the refresh token.
	/// </summary>
	public RefreshTokenHash TokenHash { get; private set; } = default!;

	/// <summary>
	/// Gets the day the token was issued, the start of the sliding expiry.
	/// </summary>
	public DateOnly LastUsedOn { get; private set; }

	/// <summary>
	/// Gets the instant after which the family can no longer rotate, however often it refreshed.
	/// </summary>
	public DateTimeOffset AbsoluteExpiryUtc { get; private set; }

	/// <summary>
	/// Gets when the token was used to obtain the next one, or <see langword="null"/> while it is unused.
	/// </summary>
	public DateTimeOffset? RotatedOn { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the session was revoked, by a sign-out or by the reuse of a token of its family.
	/// </summary>
	public bool Revoked { get; private set; }
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceSession"/> class for the persistence layer.
	/// </summary>
	private DeviceSession()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="DeviceSession"/> class.
	/// </summary>
	/// <param name="familyId">The family of the session.</param>
	/// <param name="tokenHash">The hash of the refresh token.</param>
	/// <param name="lastUsedOn">The day the token was issued.</param>
	/// <param name="absoluteExpiryUtc">The instant after which the family can no longer rotate.</param>
	private DeviceSession(Guid familyId, RefreshTokenHash tokenHash, DateOnly lastUsedOn, DateTimeOffset absoluteExpiryUtc)
		: base(DeviceSessionId.New())
	{
		FamilyId = familyId;
		TokenHash = tokenHash;
		LastUsedOn = lastUsedOn;
		AbsoluteExpiryUtc = absoluteExpiryUtc;
	}
	#endregion

	#region Internal Methods
	/// <summary>
	/// Issues a session; only <see cref="UserAccount"/> does so.
	/// </summary>
	/// <param name="familyId">The family of the session.</param>
	/// <param name="tokenHash">The hash of the refresh token.</param>
	/// <param name="lastUsedOn">The day the token was issued.</param>
	/// <param name="absoluteExpiryUtc">The instant after which the family can no longer rotate.</param>
	/// <returns>The new, unused session.</returns>
	internal static DeviceSession Issue(Guid familyId, RefreshTokenHash tokenHash, DateOnly lastUsedOn, DateTimeOffset absoluteExpiryUtc)
	{
		return new DeviceSession(familyId, tokenHash, lastUsedOn, absoluteExpiryUtc);
	}

	/// <summary>
	/// Marks the token as used, because the next one was issued.
	/// </summary>
	/// <param name="rotatedOn">When the token was used.</param>
	internal void MarkRotated(DateTimeOffset rotatedOn)
	{
		RotatedOn = rotatedOn;
	}

	/// <summary>
	/// Revokes the session; revoking twice changes nothing.
	/// </summary>
	internal void Revoke()
	{
		Revoked = true;
	}

	/// <summary>
	/// Tells whether the session can no longer rotate for age: its absolute expiry passed, or more than the sliding days went by
	/// since the day it was issued.
	/// </summary>
	/// <param name="now">The moment to judge at.</param>
	/// <param name="slidingDays">The days a session may stay unused.</param>
	/// <returns><see langword="true"/> when the session is too old.</returns>
	internal bool IsExpiredAt(DateTimeOffset now, int slidingDays)
	{
		var today = DateOnly.FromDateTime(now.UtcDateTime);
		return now >= AbsoluteExpiryUtc || today.DayNumber - LastUsedOn.DayNumber > slidingDays;
	}
	#endregion
}
