using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Tests.Features.Accounts.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="UserAccount"/>: the hash shapes, a session per sign-in, single-use rotation, the revocation
///              of a family after a reuse, the 30-day sliding and 90-day absolute expiry, sign-out and the active session count.
/// </summary>
public sealed class UserAccountTests
{
	#region Static Instances
	/// <summary>
	/// The moment of the sign-in in most tests.
	/// </summary>
	private static readonly DateTimeOffset Start = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
	#endregion

	#region Hash Tests
	/// <summary>
	/// Verifies that a hash of 64 lowercase hexadecimal characters is accepted.
	/// </summary>
	[Fact]
	public void SubjectHash_Create_Lowercase64Hex_ShouldSucceed()
	{
		var result = SubjectHash.Create(Hex('a'));

		result.IsSuccess.Should().BeTrue();
		result.Value.Value.Should().Be(Hex('a'));
	}

	/// <summary>
	/// Verifies that missing, short, upper-case and non-hexadecimal hashes are refused.
	/// </summary>
	/// <param name="text">The text to test.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("abc")]
	[InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
	[InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
	public void Hashes_Create_BadShape_ShouldFail(string? text)
	{
		SubjectHash.Create(text).Error.Should().Be(AccountErrors.SubjectHashInvalid);
		RefreshTokenHash.Create(text).Error.Should().Be(AccountErrors.RefreshTokenHashInvalid);
	}
	#endregion

	#region Create Tests
	/// <summary>
	/// Verifies that a new account has the days of the creation moment, no session and no event.
	/// </summary>
	[Fact]
	public void Create_NewAccount_ShouldHoldTodayAndNoSession()
	{
		var account = UserAccount.Create(Subject(), Start);

		account.CreatedOn.Should().Be(new DateOnly(2026, 11, 19));
		account.LastSeenOn.Should().Be(account.CreatedOn);
		account.Sessions.Should().BeEmpty();
		account.GetDomainEvents().Should().BeEmpty();
	}
	#endregion

	#region Start Session Tests
	/// <summary>
	/// Verifies that a sign-in starts a family that expires after 90 days and touches the last seen day.
	/// </summary>
	[Fact]
	public void StartSession_SignIn_ShouldStartFamilyWithAbsoluteExpiry()
	{
		var account = UserAccount.Create(Subject(), Start);
		var later = Start.AddDays(3);

		var session = account.StartSession(Token('1'), later);

		session.AbsoluteExpiryUtc.Should().Be(later.AddDays(UserAccount.AbsoluteLifetimeDays));
		session.LastUsedOn.Should().Be(new DateOnly(2026, 11, 22));
		session.RotatedOn.Should().BeNull();
		session.Revoked.Should().BeFalse();
		account.LastSeenOn.Should().Be(new DateOnly(2026, 11, 22));
		account.Sessions.Should().ContainSingle();
	}

	/// <summary>
	/// Verifies that two sign-ins start two families.
	/// </summary>
	[Fact]
	public void StartSession_TwoSignIns_ShouldStartTwoFamilies()
	{
		var account = UserAccount.Create(Subject(), Start);

		var first = account.StartSession(Token('1'), Start);
		var second = account.StartSession(Token('2'), Start);

		first.FamilyId.Should().NotBe(second.FamilyId);
		account.ActiveSessionCount(Start).Should().Be(2);
	}
	#endregion

	#region Rotate Tests
	/// <summary>
	/// Verifies that a rotation chain keeps the family and the absolute expiry and marks each old token as used.
	/// </summary>
	[Fact]
	public void Rotate_Chain_ShouldKeepFamilyAndAbsoluteExpiry()
	{
		var account = UserAccount.Create(Subject(), Start);
		var first = account.StartSession(Token('1'), Start);

		var secondResult = account.Rotate(Token('1'), Token('2'), Start.AddDays(10));
		var thirdResult = account.Rotate(Token('2'), Token('3'), Start.AddDays(20));

		secondResult.IsSuccess.Should().BeTrue();
		thirdResult.IsSuccess.Should().BeTrue();
		first.RotatedOn.Should().Be(Start.AddDays(10));
		secondResult.Value.RotatedOn.Should().Be(Start.AddDays(20));
		thirdResult.Value.RotatedOn.Should().BeNull();
		thirdResult.Value.FamilyId.Should().Be(first.FamilyId);
		thirdResult.Value.AbsoluteExpiryUtc.Should().Be(first.AbsoluteExpiryUtc);
		thirdResult.Value.LastUsedOn.Should().Be(new DateOnly(2026, 12, 9));
		account.LastSeenOn.Should().Be(new DateOnly(2026, 12, 9));
		account.ActiveSessionCount(Start.AddDays(20)).Should().Be(1);
	}

	/// <summary>
	/// Verifies that an unknown token is a not-found failure.
	/// </summary>
	[Fact]
	public void Rotate_UnknownToken_ShouldFailNotFound()
	{
		var account = UserAccount.Create(Subject(), Start);
		account.StartSession(Token('1'), Start);

		var result = account.Rotate(Token('9'), Token('2'), Start);

		result.Error.Should().Be(AccountErrors.SessionNotFound);
		result.ResultExceptionType.Should().Be(ResultExceptionType.NotFound);
		account.Sessions.Should().ContainSingle();
	}

	/// <summary>
	/// Verifies that reusing a rotated token revokes its whole family, including the newest token, but not another family.
	/// </summary>
	[Fact]
	public void Rotate_ReusedToken_ShouldRevokeItsFamilyOnly()
	{
		var account = UserAccount.Create(Subject(), Start);
		var first = account.StartSession(Token('1'), Start);
		var other = account.StartSession(Token('7'), Start);
		var second = account.Rotate(Token('1'), Token('2'), Start.AddDays(1)).Value;

		var result = account.Rotate(Token('1'), Token('3'), Start.AddDays(2));

		result.Error.Should().Be(AccountErrors.RefreshTokenReused);
		result.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
		first.Revoked.Should().BeTrue();
		second.Revoked.Should().BeTrue();
		other.Revoked.Should().BeFalse();
		account.Sessions.Should().HaveCount(3);
		account.ActiveSessionCount(Start.AddDays(2)).Should().Be(1);
	}

	/// <summary>
	/// Verifies that after a reuse even the newest token of the family is refused as revoked.
	/// </summary>
	[Fact]
	public void Rotate_NewestTokenAfterReuse_ShouldFailRevoked()
	{
		var account = UserAccount.Create(Subject(), Start);
		account.StartSession(Token('1'), Start);
		account.Rotate(Token('1'), Token('2'), Start.AddDays(1));
		account.Rotate(Token('1'), Token('3'), Start.AddDays(2));

		var result = account.Rotate(Token('2'), Token('4'), Start.AddDays(3));

		result.Error.Should().Be(AccountErrors.SessionRevoked);
		result.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
	}

	/// <summary>
	/// Verifies that a revoked token is refused and nothing is added.
	/// </summary>
	[Fact]
	public void Rotate_RevokedToken_ShouldFailRevoked()
	{
		var account = UserAccount.Create(Subject(), Start);
		account.StartSession(Token('1'), Start);
		account.EndFamilyOf(Token('1'));

		var result = account.Rotate(Token('1'), Token('2'), Start);

		result.Error.Should().Be(AccountErrors.SessionRevoked);
		account.Sessions.Should().ContainSingle();
	}

	/// <summary>
	/// Verifies that a token stays valid on the 30th day after its issue and expires on the 31st.
	/// </summary>
	[Fact]
	public void Rotate_SlidingExpiry_ShouldAllow30DaysAndRefuse31()
	{
		var account = UserAccount.Create(Subject(), Start);
		account.StartSession(Token('1'), Start);
		account.StartSession(Token('2'), Start);

		var onDay30 = account.Rotate(Token('1'), Token('3'), Start.AddDays(30));
		var onDay31 = account.Rotate(Token('2'), Token('4'), Start.AddDays(31));

		onDay30.IsSuccess.Should().BeTrue();
		onDay31.Error.Should().Be(AccountErrors.SessionExpired);
		onDay31.ResultExceptionType.Should().Be(ResultExceptionType.AuthenticationRequired);
		account.ActiveSessionCount(Start.AddDays(31)).Should().Be(1);
	}

	/// <summary>
	/// Verifies that a family that refreshes every 25 days still ends at 90 days from the sign-in.
	/// </summary>
	[Fact]
	public void Rotate_AbsoluteExpiry_ShouldEndFamilyAt90DaysDespiteRefreshes()
	{
		var account = UserAccount.Create(Subject(), Start);
		account.StartSession(Token('1'), Start);
		account.Rotate(Token('1'), Token('2'), Start.AddDays(25)).IsSuccess.Should().BeTrue();
		account.Rotate(Token('2'), Token('3'), Start.AddDays(50)).IsSuccess.Should().BeTrue();
		account.Rotate(Token('3'), Token('4'), Start.AddDays(75)).IsSuccess.Should().BeTrue();

		var atLimit = account.Rotate(Token('4'), Token('5'), Start.AddDays(90));

		atLimit.Error.Should().Be(AccountErrors.SessionExpired);
		account.ActiveSessionCount(Start.AddDays(90)).Should().Be(0);
	}

	/// <summary>
	/// Verifies that a token is still valid just before the absolute expiry moment.
	/// </summary>
	[Fact]
	public void Rotate_JustBeforeAbsoluteExpiry_ShouldSucceed()
	{
		var account = UserAccount.Create(Subject(), Start);
		account.StartSession(Token('1'), Start);
		account.Rotate(Token('1'), Token('2'), Start.AddDays(25));
		account.Rotate(Token('2'), Token('3'), Start.AddDays(50));
		account.Rotate(Token('3'), Token('4'), Start.AddDays(75));

		var result = account.Rotate(Token('4'), Token('5'), Start.AddDays(90).AddSeconds(-1));

		result.IsSuccess.Should().BeTrue();
	}
	#endregion

	#region End Family Tests
	/// <summary>
	/// Verifies that signing out by token revokes every session of the family and none of another one.
	/// </summary>
	[Fact]
	public void EndFamilyOf_Token_ShouldRevokeTheFamily()
	{
		var account = UserAccount.Create(Subject(), Start);
		account.StartSession(Token('1'), Start);
		account.Rotate(Token('1'), Token('2'), Start);
		var other = account.StartSession(Token('7'), Start);

		var result = account.EndFamilyOf(Token('2'));

		result.IsSuccess.Should().BeTrue();
		account.Sessions.Where(session => session != other).Should().OnlyContain(session => session.Revoked);
		other.Revoked.Should().BeFalse();
		account.ActiveSessionCount(Start).Should().Be(1);
	}

	/// <summary>
	/// Verifies that signing out by session identifier revokes the family.
	/// </summary>
	[Fact]
	public void EndFamilyOf_SessionId_ShouldRevokeTheFamily()
	{
		var account = UserAccount.Create(Subject(), Start);
		var session = account.StartSession(Token('1'), Start);

		var result = account.EndFamilyOf(session.Id);

		result.IsSuccess.Should().BeTrue();
		session.Revoked.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that signing out an unknown token or session is a not-found failure.
	/// </summary>
	[Fact]
	public void EndFamilyOf_Unknown_ShouldFailNotFound()
	{
		var account = UserAccount.Create(Subject(), Start);

		account.EndFamilyOf(Token('9')).ResultExceptionType.Should().Be(ResultExceptionType.NotFound);
		account.EndFamilyOf(DeviceSessionId.New()).Error.Should().Be(AccountErrors.SessionNotFound);
	}
	#endregion

	#region Active Session Count Tests
	/// <summary>
	/// Verifies that the count has one entry per family with an unused, unrevoked, unexpired token.
	/// </summary>
	[Fact]
	public void ActiveSessionCount_MixedSessions_ShouldCountLiveFamilies()
	{
		var account = UserAccount.Create(Subject(), Start);
		account.StartSession(Token('1'), Start);
		account.Rotate(Token('1'), Token('2'), Start.AddDays(1));
		account.StartSession(Token('3'), Start);
		account.StartSession(Token('4'), Start);
		account.EndFamilyOf(Token('4'));

		account.ActiveSessionCount(Start.AddDays(1)).Should().Be(2);
		account.ActiveSessionCount(Start.AddDays(40)).Should().Be(0);
	}
	#endregion

	#region Version Tests
	/// <summary>
	/// Verifies that every state change raises the version, and that a refused rotation that changes nothing leaves it alone.
	/// </summary>
	[Fact]
	public void Version_EveryStateChange_ShouldIncrease()
	{
		var account = UserAccount.Create(Subject(), Start);
		account.Version.Should().Be(0);

		account.StartSession(Token('1'), Start);
		account.Version.Should().Be(1);
		account.Rotate(Token('1'), Token('2'), Start.AddDays(1));
		account.Version.Should().Be(2);
		account.Rotate(Token('9'), Token('3'), Start.AddDays(1));
		account.Version.Should().Be(2);
		account.Rotate(Token('1'), Token('3'), Start.AddDays(1));
		account.Version.Should().Be(3);
		account.EndFamilyOf(Token('2'));
		account.Version.Should().Be(4);
		account.EndFamilyOf(Token('9'));
		account.Version.Should().Be(4);
	}
	#endregion

	#region Offset Tests
	/// <summary>
	/// Verifies that a moment with an offset is read as UTC: the stored expiry has offset zero and the day is the UTC day.
	/// </summary>
	[Fact]
	public void Behaviours_NonUtcMoment_ShouldBeNormalisedToUtc()
	{
		var local = new DateTimeOffset(2026, 11, 20, 0, 30, 0, TimeSpan.FromHours(2));
		var account = UserAccount.Create(Subject(), local);

		var session = account.StartSession(Token('1'), local);
		var next = account.Rotate(Token('1'), Token('2'), local.AddDays(1)).Value;

		account.CreatedOn.Should().Be(new DateOnly(2026, 11, 19));
		session.LastUsedOn.Should().Be(new DateOnly(2026, 11, 19));
		session.AbsoluteExpiryUtc.Offset.Should().Be(TimeSpan.Zero);
		session.RotatedOn!.Value.Offset.Should().Be(TimeSpan.Zero);
		next.LastUsedOn.Should().Be(new DateOnly(2026, 11, 20));
		account.ActiveSessionCount(local.AddDays(1)).Should().Be(1);
	}
	#endregion

	#region Session Cap Tests
	/// <summary>
	/// Verifies that a sixth sign-in revokes the family that started first and leaves the other four untouched.
	/// </summary>
	[Fact]
	public void StartSession_SixthFamily_ShouldRevokeTheOldestOnly()
	{
		var account = UserAccount.Create(Subject(), Start);
		var sessions = Enumerable.Range(0, UserAccount.MaximumActiveFamilies)
			.Select(index => account.StartSession(Token((char)('1' + index)), Start.AddHours(index)))
			.ToList();

		var sixth = account.StartSession(Token('9'), Start.AddHours(10));

		sessions[0].Revoked.Should().BeTrue();
		sessions.Skip(1).Should().OnlyContain(session => !session.Revoked);
		sixth.Revoked.Should().BeFalse();
		account.ActiveSessionCount(Start.AddHours(10)).Should().Be(UserAccount.MaximumActiveFamilies);
	}

	/// <summary>
	/// Verifies that revoked, rotated-away and expired families do not count towards the cap, so nothing is revoked.
	/// </summary>
	[Fact]
	public void StartSession_InactiveFamilies_ShouldNotCountTowardsTheCap()
	{
		var account = UserAccount.Create(Subject(), Start);
		var signedOut = account.StartSession(Token('1'), Start);
		account.EndFamilyOf(Token('1'));
		var expired = account.StartSession(Token('2'), Start);
		var live = Enumerable.Range(0, UserAccount.MaximumActiveFamilies - 1)
			.Select(index => account.StartSession(Token((char)('3' + index)), Start.AddDays(40)))
			.ToList();

		var next = account.StartSession(Token('9'), Start.AddDays(40));

		signedOut.Revoked.Should().BeTrue();
		expired.Revoked.Should().BeFalse();
		live.Should().OnlyContain(session => !session.Revoked);
		next.Revoked.Should().BeFalse();
		account.ActiveSessionCount(Start.AddDays(40)).Should().Be(UserAccount.MaximumActiveFamilies);
	}
	#endregion

	#region Expiry Boundary Tests
	/// <summary>
	/// Verifies that the sliding expiry still allows a rotation after 29 and after 30 days.
	/// </summary>
	/// <param name="days">The days since the token was issued.</param>
	[Theory]
	[InlineData(29)]
	[InlineData(30)]
	public void Rotate_WithinSlidingLifetime_ShouldSucceed(int days)
	{
		var account = UserAccount.Create(Subject(), Start);
		account.StartSession(Token('1'), Start);

		account.Rotate(Token('1'), Token('2'), Start.AddDays(days)).IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that the sliding expiry counts UTC days: a token issued at 23:59 UTC is one day old at 00:00 the next day, so 30 days and one minute is already 31 days.
	/// </summary>
	[Fact]
	public void Rotate_AcrossUtcMidnight_ShouldCountOneDay()
	{
		var lateEvening = new DateTimeOffset(2026, 11, 1, 23, 59, 0, TimeSpan.Zero);
		var account = UserAccount.Create(Subject(), lateEvening);
		account.StartSession(Token('1'), lateEvening);
		account.StartSession(Token('2'), lateEvening);
		var thirtyDaysByCalendar = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
		var thirtyOneDaysByCalendar = new DateTimeOffset(2026, 12, 2, 0, 0, 0, TimeSpan.Zero);

		var onDay30 = account.Rotate(Token('1'), Token('3'), thirtyDaysByCalendar);
		var onDay31 = account.Rotate(Token('2'), Token('4'), thirtyOneDaysByCalendar);

		onDay30.IsSuccess.Should().BeTrue();
		onDay31.Error.Should().Be(AccountErrors.SessionExpired, "only 30 days and one minute passed, but the UTC day count is 31");
	}

	/// <summary>
	/// Verifies that rotating a token of a family that was revoked already is refused and changes nothing.
	/// </summary>
	[Fact]
	public void Rotate_RevokedFamily_ShouldFailRevokedAndChangeNothing()
	{
		var account = UserAccount.Create(Subject(), Start);
		var session = account.StartSession(Token('1'), Start);
		account.EndFamilyOf(Token('1'));
		var version = account.Version;

		var result = account.Rotate(Token('1'), Token('2'), Start.AddDays(1));

		result.Error.Should().Be(AccountErrors.SessionRevoked);
		account.Sessions.Should().ContainSingle();
		session.RotatedOn.Should().BeNull();
		account.Version.Should().Be(version);
		account.LastSeenOn.Should().Be(new DateOnly(2026, 11, 19));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a hash text of one repeated character.
	/// </summary>
	/// <param name="character">The hexadecimal character.</param>
	/// <returns>64 copies of the character.</returns>
	private static string Hex(char character) => new(character, 64);

	/// <summary>
	/// Builds a subject hash.
	/// </summary>
	/// <returns>A valid subject hash.</returns>
	private static SubjectHash Subject() => SubjectHash.Create(Hex('a')).Value;

	/// <summary>
	/// Builds a refresh token hash.
	/// </summary>
	/// <param name="character">The hexadecimal character that tells the tokens apart.</param>
	/// <returns>A valid refresh token hash.</returns>
	private static RefreshTokenHash Token(char character) => RefreshTokenHash.Create(Hex(character)).Value;
	#endregion
}
