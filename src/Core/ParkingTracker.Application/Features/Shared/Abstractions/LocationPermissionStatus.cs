namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the user allowed the app to know about the position: while the app is in use, and all the time (also with
///              the app off screen, which the parking alerts of a later version need).
/// </summary>
/// <param name="Foreground">The permission to read the position while the app is in use.</param>
/// <param name="Background">The permission to read the position when the app is not in use ("Allow all the time").</param>
public sealed record LocationPermissionStatus(PermissionState Foreground, PermissionState Background);
