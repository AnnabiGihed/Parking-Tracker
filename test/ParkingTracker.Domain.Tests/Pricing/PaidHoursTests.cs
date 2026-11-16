using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.Tests.TestDoubles;

namespace ParkingTracker.Domain.Tests.Pricing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="BelgianHolidays"/>, <see cref="BrusselsTime"/> and <see cref="PaidHours"/>:
///              <c>isHoliday</c>, <c>brussels</c>, <c>paidNow</c> and <c>nextPaid</c> of live.js.
/// </summary>
public sealed class PaidHoursTests
{
	#region Holiday Tests
	/// <summary>
	/// Verifies the Easter Sunday of known years.
	/// </summary>
	/// <param name="year">The year.</param>
	/// <param name="month">The month of Easter Sunday.</param>
	/// <param name="day">The day of Easter Sunday.</param>
	[Theory]
	[InlineData(2024, 3, 31)]
	[InlineData(2025, 4, 20)]
	[InlineData(2026, 4, 5)]
	[InlineData(2038, 4, 25)]
	public void EasterSunday_KnownYear_ShouldMatchTheCalendar(int year, int month, int day)
	{
		BelgianHolidays.EasterSunday(year).Should().Be(new DateOnly(year, month, day));
	}

	/// <summary>
	/// Verifies the holidays of 2026: the fixed days and the days that follow Easter.
	/// </summary>
	/// <param name="month">The month.</param>
	/// <param name="day">The day.</param>
	[Theory]
	[InlineData(1, 1)]
	[InlineData(4, 6)]
	[InlineData(5, 1)]
	[InlineData(5, 14)]
	[InlineData(5, 25)]
	[InlineData(7, 21)]
	[InlineData(8, 15)]
	[InlineData(11, 1)]
	[InlineData(11, 11)]
	[InlineData(12, 25)]
	public void IsHoliday_HolidaysOf2026_ShouldBeTrue(int month, int day)
	{
		BelgianHolidays.IsHoliday(new DateOnly(2026, month, day)).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that ordinary days, Easter Sunday itself and Good Friday are not holidays.
	/// </summary>
	/// <param name="month">The month.</param>
	/// <param name="day">The day.</param>
	[Theory]
	[InlineData(4, 5)]
	[InlineData(4, 3)]
	[InlineData(4, 7)]
	[InlineData(5, 13)]
	[InlineData(5, 15)]
	[InlineData(11, 16)]
	[InlineData(12, 26)]
	public void IsHoliday_OrdinaryDaysOf2026_ShouldBeFalse(int month, int day)
	{
		BelgianHolidays.IsHoliday(new DateOnly(2026, month, day)).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that Easter Monday of 2025 is a holiday.
	/// </summary>
	[Fact]
	public void IsHoliday_EasterMondayOf2025_ShouldBeTrue()
	{
		BelgianHolidays.IsHoliday(new DateOnly(2025, 4, 21)).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that the Easter dates move with the year: 21 April is an ordinary day in 2026.
	/// </summary>
	[Fact]
	public void IsHoliday_SameDateAsEasterMondayOf2025_ShouldBeFalseIn2026()
	{
		BelgianHolidays.IsHoliday(new DateOnly(2026, 4, 21)).Should().BeFalse();
	}
	#endregion

	#region BrusselsTime Tests
	/// <summary>
	/// Verifies the winter offset (UTC+1) and the summer offset (UTC+2) and that the date and weekday follow Brussels.
	/// </summary>
	[Fact]
	public void At_WinterAndSummerInstants_ShouldUseTheBrusselsOffset()
	{
		var winter = BrusselsTime.At(RuleBuilder.At("2026-11-16T09:00:00Z"));
		var summer = BrusselsTime.At(RuleBuilder.At("2026-07-14T09:00:00Z"));
		var nearMidnight = BrusselsTime.At(RuleBuilder.At("2026-11-15T23:30:00Z"));

		winter.Should().Be(new BrusselsTime(new DateOnly(2026, 11, 16), DayOfWeek.Monday, 600));
		summer.Should().Be(new BrusselsTime(new DateOnly(2026, 7, 14), DayOfWeek.Tuesday, 660));
		nearMidnight.Should().Be(new BrusselsTime(new DateOnly(2026, 11, 16), DayOfWeek.Monday, 30));
	}

	/// <summary>
	/// Verifies that the Brussels time zone resolves on this platform and has daylight-saving time. When the platform
	/// knows neither the IANA nor the Windows identifier the type fails to load instead of guessing an offset.
	/// </summary>
	[Fact]
	public void Zone_Brussels_ShouldResolveWithDaylightSaving()
	{
		BrusselsTime.Zone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(1));
		BrusselsTime.Zone.SupportsDaylightSavingTime.Should().BeTrue();
	}
	#endregion

	#region IsPaidAt Tests
	/// <summary>
	/// Verifies Monday 10:00, Monday 08:30, Sunday noon and Saturday 20:59 under a Monday to Saturday 09:00 to 21:00 rule.
	/// </summary>
	/// <param name="instant">The instant in UTC (Brussels is UTC+1 in November).</param>
	/// <param name="expected">Whether payment is required.</param>
	[Theory]
	[InlineData("2026-11-16T09:00:00Z", true)]
	[InlineData("2026-11-16T07:30:00Z", false)]
	[InlineData("2026-11-15T11:00:00Z", false)]
	[InlineData("2026-11-14T19:59:00Z", true)]
	[InlineData("2026-11-14T20:00:00Z", false)]
	[InlineData("2026-11-16T08:00:00Z", true)]
	public void IsPaidAt_StandardRule_ShouldFollowDaysAndHours(string instant, bool expected)
	{
		PaidHours.IsPaidAt(RuleBuilder.Create(), RuleBuilder.At(instant)).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that the hours are read on the Brussels clock across the daylight-saving change of 29 March 2026.
	/// </summary>
	/// <param name="instant">The instant in UTC.</param>
	/// <param name="expected">Whether payment is required.</param>
	[Theory]
	[InlineData("2026-03-30T07:15:00Z", true)]
	[InlineData("2026-03-30T06:45:00Z", false)]
	[InlineData("2026-03-27T07:45:00Z", false)]
	public void IsPaidAt_AroundTheSpringChange_ShouldUseTheBrusselsClock(string instant, bool expected)
	{
		PaidHours.IsPaidAt(RuleBuilder.Create(), RuleBuilder.At(instant)).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that no rule means paid, even on a Sunday.
	/// </summary>
	[Fact]
	public void IsPaidAt_NoRule_ShouldAssumePaid()
	{
		PaidHours.IsPaidAt(null, RuleBuilder.At("2026-11-15T11:00:00Z")).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a free rule means not paid, even in the paid hours.
	/// </summary>
	[Fact]
	public void IsPaidAt_FreeRule_ShouldNotBePaid()
	{
		PaidHours.IsPaidAt(RuleBuilder.Create(ZoneType.Free), RuleBuilder.At("2026-11-16T09:00:00Z")).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a public holiday is free when the rule does not apply on holidays.
	/// </summary>
	/// <param name="instant">The instant in UTC: Armistice Day, then Easter Monday.</param>
	[Theory]
	[InlineData("2026-11-11T10:00:00Z")]
	[InlineData("2026-04-06T10:00:00Z")]
	public void IsPaidAt_PublicHoliday_ShouldBeFree(string instant)
	{
		PaidHours.IsPaidAt(RuleBuilder.Create(), RuleBuilder.At(instant)).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a public holiday is paid when the rule applies on holidays.
	/// </summary>
	[Fact]
	public void IsPaidAt_PublicHolidayWithRuleOnHolidays_ShouldBePaid()
	{
		PaidHours.IsPaidAt(RuleBuilder.Create(paidOnHolidays: true), RuleBuilder.At("2026-11-11T10:00:00Z")).Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a paid day with unknown hours is paid all day.
	/// </summary>
	/// <param name="start">The start hour.</param>
	/// <param name="end">The end hour.</param>
	[Theory]
	[InlineData(null, null)]
	[InlineData("09:00:00", null)]
	[InlineData(null, "21:00:00")]
	[InlineData("soon", "later")]
	public void IsPaidAt_UnknownHours_ShouldBePaidOnPaidDays(string? start, string? end)
	{
		var rule = RuleBuilder.Create(start: start, end: end);

		PaidHours.IsPaidAt(rule, RuleBuilder.At("2026-11-16T03:00:00Z")).Should().BeTrue();
		PaidHours.IsPaidAt(rule, RuleBuilder.At("2026-11-15T11:00:00Z")).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that hours given as <c>HH:mm</c> without seconds are read too.
	/// </summary>
	[Fact]
	public void IsPaidAt_HoursWithoutSeconds_ShouldBeRead()
	{
		var rule = RuleBuilder.Create(start: "9:00", end: "18:30");

		PaidHours.IsPaidAt(rule, RuleBuilder.At("2026-11-16T17:29:00Z")).Should().BeTrue();
		PaidHours.IsPaidAt(rule, RuleBuilder.At("2026-11-16T17:30:00Z")).Should().BeFalse();
	}
	#endregion

	#region NextPaid Tests
	/// <summary>
	/// Verifies the labels: today, tomorrow and a weekday name with the start hour.
	/// </summary>
	/// <param name="instant">The instant in UTC.</param>
	/// <param name="expected">The expected label.</param>
	[Theory]
	[InlineData("2026-11-16T07:30:00Z", "today 09:00")]
	[InlineData("2026-11-15T11:00:00Z", "tomorrow 09:00")]
	[InlineData("2026-11-14T19:59:00Z", "Monday 09:00")]
	[InlineData("2026-11-16T09:00:00Z", "tomorrow 09:00")]
	[InlineData("2026-11-10T19:00:00Z", "Thursday 09:00")]
	public void NextPaid_StandardRule_ShouldLabelTheNextStart(string instant, string expected)
	{
		PaidHours.NextPaid(RuleBuilder.Create(), RuleBuilder.At(instant)).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that no rule gives no label.
	/// </summary>
	[Fact]
	public void NextPaid_NoRule_ShouldReturnNull()
	{
		PaidHours.NextPaid(null, RuleBuilder.At("2026-11-16T09:00:00Z")).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a rule without a start hour gives no label.
	/// </summary>
	[Fact]
	public void NextPaid_NoStartHour_ShouldReturnNull()
	{
		PaidHours.NextPaid(RuleBuilder.Create(start: null), RuleBuilder.At("2026-11-16T09:00:00Z")).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a rule that applies on no day gives no label.
	/// </summary>
	[Fact]
	public void NextPaid_NoPaidDay_ShouldReturnNull()
	{
		var rule = RuleBuilder.Create(paidDays: new HashSet<DayOfWeek>());

		PaidHours.NextPaid(rule, RuleBuilder.At("2026-11-16T09:00:00Z")).Should().BeNull();
	}

	/// <summary>
	/// Verifies that the Saturday before the spring change, at 23:30 in Brussels, points to Monday: Sunday is not paid.
	/// </summary>
	[Fact]
	public void NextPaid_SaturdayNightBeforeSpringForward_ShouldNameMonday()
	{
		var instant = RuleBuilder.At("2026-03-28T22:30:00Z");

		PaidHours.NextPaid(RuleBuilder.Create(), instant).Should().Be("Monday 09:00");
	}

	/// <summary>
	/// Verifies that on the Saturday before the spring change a Sunday rule is "tomorrow", although the next day lasts
	/// 23 hours, so adding 24 hours would land on Monday.
	/// </summary>
	[Fact]
	public void NextPaid_SundayRuleOnSaturdayNightBeforeSpringForward_ShouldBeTomorrow()
	{
		var rule = RuleBuilder.Create(paidDays: new HashSet<DayOfWeek> { DayOfWeek.Sunday });
		var instant = RuleBuilder.At("2026-03-28T22:30:00Z");

		PaidHours.NextPaid(rule, instant).Should().Be("tomorrow 09:00");
	}

	/// <summary>
	/// Verifies that at 00:30 on the autumn change Sunday (a 25-hour day) a Monday rule is "tomorrow".
	/// </summary>
	[Fact]
	public void NextPaid_MondayRuleOnAutumnChangeSundayAfterMidnight_ShouldBeTomorrow()
	{
		var rule = RuleBuilder.Create(paidDays: new HashSet<DayOfWeek> { DayOfWeek.Monday });
		var instant = RuleBuilder.At("2026-10-24T22:30:00Z");

		PaidHours.NextPaid(rule, instant).Should().Be("tomorrow 09:00");
	}

	/// <summary>
	/// Verifies that at 00:30 on the autumn change Sunday a Sunday rule is "today", before its start hour.
	/// </summary>
	[Fact]
	public void NextPaid_SundayRuleOnAutumnChangeSundayAfterMidnight_ShouldBeToday()
	{
		var rule = RuleBuilder.Create(paidDays: new HashSet<DayOfWeek> { DayOfWeek.Sunday });
		var instant = RuleBuilder.At("2026-10-24T22:30:00Z");

		PaidHours.NextPaid(rule, instant).Should().Be("today 09:00");
	}

	/// <summary>
	/// Verifies that a rule that applies on one weekday only is found within the 9 days.
	/// </summary>
	[Fact]
	public void NextPaid_SingleDayRule_ShouldNameThatWeekday()
	{
		var rule = RuleBuilder.Create(paidDays: new HashSet<DayOfWeek> { DayOfWeek.Sunday });

		PaidHours.NextPaid(rule, RuleBuilder.At("2026-11-16T09:00:00Z")).Should().Be("Sunday 09:00");
	}
	#endregion

	#region AppliesOn Tests
	/// <summary>
	/// Verifies that a missing rule is refused.
	/// </summary>
	[Fact]
	public void AppliesOn_NullRule_ShouldThrow()
	{
		var act = () => PaidHours.AppliesOn(null!, BrusselsTime.At(RuleBuilder.At("2026-11-16T09:00:00Z"))); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
