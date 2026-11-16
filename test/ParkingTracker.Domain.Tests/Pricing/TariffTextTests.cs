using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.Tests.TestDoubles;

namespace ParkingTracker.Domain.Tests.Pricing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="TariffText"/>: <c>tariffText</c>, <c>scheduleText</c>, <c>euro</c> and <c>minutesText</c> of live.js.
/// </summary>
public sealed class TariffTextTests
{
	#region Test Infrastructure
	private static ParkingRule With(
		ZoneType? type = ZoneType.Payable,
		IReadOnlySet<DayOfWeek>? days = null,
		decimal? charge = null,
		int? free = null,
		int? max = null,
		decimal? fee = null,
		string? start = "09:00:00",
		string? end = "21:00:00")
	{
		return new ParkingRule(type, null, days ?? RuleBuilder.MondayToSaturday, start, end, false, charge, free, max, fee);
	}
	#endregion

	#region Describe Tests
	/// <summary>
	/// Verifies the full tariff text of a red zone.
	/// </summary>
	[Fact]
	public void Describe_FullPaidRule_ShouldListPriceFreeTimeMaxScheduleAndFine()
	{
		TariffText.Describe(RuleBuilder.Create()).Should().Be("€3,60/h · first 20 min free · max 4 h 30 · Mon–Sat 09:00–21:00 · fine €47,00");
	}

	/// <summary>
	/// Verifies that a blue zone shows "Blue disc" instead of a price, whatever the charge.
	/// </summary>
	[Fact]
	public void Describe_DiskRule_ShouldShowBlueDiscInsteadOfPrice()
	{
		var rule = With(ZoneType.Disk, charge: 2m, max: 120, start: "09:00:00", end: "18:00:00");

		TariffText.Describe(rule).Should().Be("Blue disc · max 2 h · Mon–Sat 09:00–18:00");
	}

	/// <summary>
	/// Verifies that a rule with nothing but a schedule shows only the schedule, and no rule gives no text.
	/// </summary>
	[Fact]
	public void Describe_ScheduleOnlyOrNoRule_ShouldShowScheduleOrNull()
	{
		TariffText.Describe(With()).Should().Be("Mon–Sat 09:00–21:00");
		TariffText.Describe(null).Should().BeNull();
	}

	/// <summary>
	/// Verifies that a zero charge is shown as a price, as in live.js, while a zero free time, a zero maximum and a missing fine are left out.
	/// </summary>
	[Fact]
	public void Describe_ZeroCharge_ShouldShowZeroEuroButOmitZeroFreeTimeAndMax()
	{
		var rule = With(charge: 0m, free: 0, max: 0);

		TariffText.Describe(rule).Should().Be("€0,00/h · Mon–Sat 09:00–21:00");
	}
	#endregion

	#region Euro Tests
	/// <summary>
	/// Verifies the euro format with two decimals and a comma.
	/// </summary>
	/// <param name="amount">The amount.</param>
	/// <param name="expected">The expected text.</param>
	[Theory]
	[InlineData(3.6, "€3,60")]
	[InlineData(47, "€47,00")]
	[InlineData(0.5, "€0,50")]
	[InlineData(1234.5, "€1234,50")]
	public void Euro_Amount_ShouldUseTwoDecimalsAndComma(double amount, string expected)
	{
		TariffText.Euro((decimal)amount).Should().Be(expected);
	}

	/// <summary>
	/// Verifies that a decimal with two decimals, as the API sends it, is written unchanged.
	/// </summary>
	[Fact]
	public void Euro_TwoDecimalAmount_ShouldBeWrittenUnchanged()
	{
		TariffText.Euro(2.75m).Should().Be("€2,75");
	}

	/// <summary>
	/// Verifies that a midpoint beyond two decimals is rounded away from zero.
	/// </summary>
	[Fact]
	public void Euro_MidpointBeyondTwoDecimals_ShouldRoundAwayFromZero()
	{
		TariffText.Euro(2.675m).Should().Be("€2,68");
	}
	#endregion

	#region Maximum Stay Tests
	/// <summary>
	/// Verifies the maximum stay text for minutes, whole hours and hours with minutes.
	/// </summary>
	/// <param name="minutes">The maximum stay in minutes.</param>
	/// <param name="expected">The expected part of the text.</param>
	[Theory]
	[InlineData(20, "max 20 min")]
	[InlineData(60, "max 1 h")]
	[InlineData(90, "max 1 h 30")]
	[InlineData(120, "max 2 h")]
	[InlineData(270, "max 4 h 30")]
	[InlineData(45, "max 45 min")]
	public void Describe_MaxTime_ShouldWriteMinutesAndHours(int minutes, string expected)
	{
		TariffText.Describe(With(max: minutes)).Should().StartWith(expected);
	}
	#endregion

	#region Schedule Tests
	/// <summary>
	/// Verifies the day spans: Monday to Saturday, every day and a list of days.
	/// </summary>
	[Fact]
	public void Schedule_DaySets_ShouldUseSpanOrList()
	{
		var weekdays = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
		var everyDay = new HashSet<DayOfWeek>(RuleBuilder.MondayToSaturday) { DayOfWeek.Sunday };
		var sundaysAndSaturdays = new HashSet<DayOfWeek> { DayOfWeek.Sunday, DayOfWeek.Saturday };

		TariffText.Schedule(With(days: weekdays)).Should().Be("Mon, Tue, Wed, Thu, Fri 09:00–21:00");
		TariffText.Schedule(With(days: everyDay)).Should().Be("every day 09:00–21:00");
		TariffText.Schedule(With(days: sundaysAndSaturdays)).Should().Be("Sat, Sun 09:00–21:00");
		TariffText.Schedule(With()).Should().Be("Mon–Sat 09:00–21:00");
	}

	/// <summary>
	/// Verifies that the hours are left out when either end is unknown and that hours without seconds are kept as they are.
	/// </summary>
	[Fact]
	public void Schedule_UnknownOrShortHours_ShouldOmitOrKeepThem()
	{
		TariffText.Schedule(With(start: null, end: null)).Should().Be("Mon–Sat");
		TariffText.Schedule(With(end: null)).Should().Be("Mon–Sat");
		TariffText.Schedule(With(start: "09:00", end: "18:30")).Should().Be("Mon–Sat 09:00–18:30");
	}

	/// <summary>
	/// Verifies that a missing rule is refused.
	/// </summary>
	[Fact]
	public void Schedule_Null_ShouldThrow()
	{
		var act = () => TariffText.Schedule(null!); // exercising the guard clause

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
