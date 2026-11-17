namespace ParkingTracker.Application.Features.ParkingDetection.Commands.HandleParked;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What <see cref="HandleParkedCommand"/> did: the headline that was saved with the car and how the user was told.
/// </summary>
/// <param name="Headline">The friendly headline of the answer, for example <c>Paid parking</c>.</param>
/// <param name="Delivery">How the user was told.</param>
public sealed record ParkedOutcome(string Headline, ParkedDelivery Delivery);
