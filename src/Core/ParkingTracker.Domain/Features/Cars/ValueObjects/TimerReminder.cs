namespace ParkingTracker.Domain.Features.Cars.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One notification of the parking timer, to be shown at a given moment. The ids are the ones of the first version of
///              the app, so that a reminder scheduled by an older build is replaced instead of shown twice.
/// </summary>
/// <param name="Id">The id of the notification and of its alarm: <see cref="ParkingTimer.WarningId"/> or <see cref="ParkingTimer.EndId"/>.</param>
/// <param name="At">When to show it.</param>
/// <param name="Body">The text of the notification.</param>
public sealed record TimerReminder(int Id, DateTimeOffset At, string Body);
