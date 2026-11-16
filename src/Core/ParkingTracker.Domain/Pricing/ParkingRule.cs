namespace ParkingTracker.Domain.Pricing;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The parking rule of a zone as published by Brussels Mobility: when paying (or a disc) is required and what it costs.
/// </summary>
/// <param name="Type">The kind of zone the rule describes, or <see langword="null"/> when the rule does not say.</param>
/// <param name="Name">Name of the rule, or <see langword="null"/>.</param>
/// <param name="PaidDays">The weekdays on which the rule applies.</param>
/// <param name="StartHour">Start of the paid hours as <c>HH:mm</c> or <c>HH:mm:ss</c>, or <see langword="null"/> when unknown.</param>
/// <param name="EndHour">End of the paid hours in the same format, or <see langword="null"/> when unknown.</param>
/// <param name="PaidOnHolidays">Whether the rule also applies on Belgian public holidays.</param>
/// <param name="Charge60">Price per hour in euros, or <see langword="null"/>.</param>
/// <param name="FreeTime">Minutes that are free at the start, or <see langword="null"/>.</param>
/// <param name="MaxTime">Maximum stay in minutes, or <see langword="null"/>.</param>
/// <param name="Fee">Fine in euros, or <see langword="null"/>.</param>
public sealed record ParkingRule(
	ZoneType? Type,
	string? Name,
	IReadOnlySet<DayOfWeek> PaidDays,
	string? StartHour,
	string? EndHour,
	bool PaidOnHolidays,
	decimal? Charge60,
	int? FreeTime,
	int? MaxTime,
	decimal? Fee);
