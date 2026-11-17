namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The position of the user inside <see cref="ScreenStateAnswer"/>: where, how exact, and how old the fix is by the clock
///              of the device, which shows a clock that runs apart from the time stamps of the location source.
/// </summary>
/// <param name="Lat">Degrees north.</param>
/// <param name="Lon">Degrees east.</param>
/// <param name="Acc">The accuracy in metres.</param>
/// <param name="AgeSeconds">Seconds between the time stamp of the fix and now, rounded to one decimal.</param>
public sealed record MyPositionAnswer(double Lat, double Lon, double Acc, double AgeSeconds);
