namespace ParkingTracker.Domain.Features.Cars.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The parking timer of the user: when the paid time ends and how many minutes were asked for. It is saved as the
///              <c>pt.timer</c> value of the first version of the app and plans the two reminders: 10 minutes before the end and at
///              the end, with the texts of the first version.
/// </summary>
/// <param name="End">When the parking time ends.</param>
/// <param name="Minutes">The number of minutes the timer was started with.</param>
public sealed record ParkingTimer(DateTimeOffset End, int Minutes)
{
	#region Constants
	/// <summary>
	/// The notification id of the reminder 10 minutes before the end.
	/// </summary>
	public const int WarningId = 4101;

	/// <summary>
	/// The notification id of the reminder at the end.
	/// </summary>
	public const int EndId = 4102;

	/// <summary>
	/// The shortest timer a user can start, in minutes.
	/// </summary>
	public const int MinimumMinutes = 5;

	/// <summary>
	/// The longest timer a user can start, in minutes (one day).
	/// </summary>
	public const int MaximumMinutes = 1440;

	/// <summary>
	/// The text of the reminder 10 minutes before the end.
	/// </summary>
	public const string WarningText = "Your parking time ends in 10 minutes.";

	/// <summary>
	/// The text of the reminder at the end.
	/// </summary>
	public const string EndText = "Your parking time is over.";
	#endregion

	#region Static Instances
	/// <summary>
	/// How long before the end the warning comes; it is also the span in which the timer shows as late.
	/// </summary>
	public static readonly TimeSpan WarningBefore = TimeSpan.FromMinutes(10);

	/// <summary>
	/// A reminder closer than this to the moment of planning is not scheduled: it would arrive before the user sees the timer.
	/// </summary>
	public static readonly TimeSpan MinimumLead = TimeSpan.FromSeconds(5);
	#endregion

	#region Factory Methods
	/// <summary>
	/// Starts a timer of the given length; a length outside 5 minutes to one day (zero included) is moved to the nearest limit, as
	/// the form of the first version did.
	/// </summary>
	/// <param name="minutes">The length the user asked for.</param>
	/// <param name="now">The moment the timer starts.</param>
	/// <returns>The timer.</returns>
	public static ParkingTimer StartingAt(int minutes, DateTimeOffset now)
	{
		var clamped = Math.Clamp(minutes, MinimumMinutes, MaximumMinutes);
		return new ParkingTimer(now.AddMinutes(clamped), clamped);
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Gets the time left.
	/// </summary>
	/// <param name="now">The current moment.</param>
	/// <returns>The time left; zero or negative once the end passed.</returns>
	public TimeSpan Remaining(DateTimeOffset now) => End - now;

	/// <summary>
	/// Tells whether the time is nearly or already over: 10 minutes or less are left.
	/// </summary>
	/// <param name="now">The current moment.</param>
	/// <returns><see langword="true"/> when the timer is late.</returns>
	public bool IsLate(DateTimeOffset now) => Remaining(now) <= WarningBefore;

	/// <summary>
	/// Plans the reminders still worth showing: the warning and the end, each only when it is more than 5 seconds away.
	/// </summary>
	/// <param name="now">The current moment.</param>
	/// <returns>The reminders, earliest first; empty when both are too close or past.</returns>
	public IReadOnlyList<TimerReminder> Plan(DateTimeOffset now)
	{
		TimerReminder[] all = [new(WarningId, End - WarningBefore, WarningText), new(EndId, End, EndText)];
		return all.Where(reminder => reminder.At > now + MinimumLead).ToArray();
	}
	#endregion
}
