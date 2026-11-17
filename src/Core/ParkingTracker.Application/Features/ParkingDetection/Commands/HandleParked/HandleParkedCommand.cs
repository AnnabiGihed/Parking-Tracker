using Pivot.Framework.Application.Abstractions.Messaging.Commands;

namespace ParkingTracker.Application.Features.ParkingDetection.Commands.HandleParked;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Says that the user has parked at a place: the app decides the spot, saves the car there and sends one notification
///              with the answer and, when parking is not free, the nearest spot where the card works.
/// </summary>
/// <param name="Latitude">The latitude in degrees, -90 to 90.</param>
/// <param name="Longitude">The longitude in degrees, -180 to 180.</param>
/// <param name="AccuracyMeters">The GPS accuracy in metres, or <see langword="null"/> when the device reported none.</param>
public sealed record HandleParkedCommand(double Latitude, double Longitude, double? AccuracyMeters) : ICommand;
