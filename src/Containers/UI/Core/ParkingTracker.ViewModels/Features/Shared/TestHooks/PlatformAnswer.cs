namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the <c>ptTest.platformStatus</c> hook returns to the emulator test: what the device allows, whether it is online, and the state of the parking alerts: the background service, the
///              notifications and the exact alarms.
/// </summary>
/// <param name="LocationForeground">The foreground location permission: <c>granted</c>, <c>denied</c> or <c>unknown</c>.</param>
/// <param name="LocationBackground">The background ("all the time") location permission: <c>granted</c>, <c>denied</c> or <c>unknown</c>.</param>
/// <param name="Online">Whether the device has an internet connection.</param>
/// <param name="Service">Whether the background service that follows the position for the parking alerts is running.</param>
/// <param name="Notifications">Whether the app may show notifications.</param>
/// <param name="ExactAlarms">Whether the app may schedule exact alarms.</param>
public sealed record PlatformAnswer(string LocationForeground, string LocationBackground, bool Online, bool Service, bool Notifications, bool ExactAlarms);
