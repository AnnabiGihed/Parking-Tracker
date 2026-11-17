namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the <c>ptTest.platformStatus</c> hook returns to the emulator test: what the device allows and whether it is online.
/// </summary>
/// <param name="LocationForeground">The foreground location permission: <c>granted</c>, <c>denied</c> or <c>unknown</c>.</param>
/// <param name="LocationBackground">The background ("all the time") location permission: <c>granted</c>, <c>denied</c> or <c>unknown</c>.</param>
/// <param name="Online">Whether the device has an internet connection.</param>
public sealed record PlatformAnswer(string LocationForeground, string LocationBackground, bool Online);
