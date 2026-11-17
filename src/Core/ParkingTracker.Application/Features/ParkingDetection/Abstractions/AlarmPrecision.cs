namespace ParkingTracker.Application.Features.ParkingDetection.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : How exactly the phone will deliver the timer reminders that were scheduled.
/// </summary>
public enum AlarmPrecision
{
	/// <summary>
	/// Each reminder comes at its moment.
	/// </summary>
	Exact,

	/// <summary>
	/// The system may delay a reminder by some minutes, because the app may not schedule exact alarms.
	/// </summary>
	Inexact,
}
