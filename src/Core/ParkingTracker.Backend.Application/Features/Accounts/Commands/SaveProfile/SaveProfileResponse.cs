namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.SaveProfile;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The result of a stored profile: the revision it is stored under.
/// </summary>
/// <param name="Revision">The revision of the stored document, 1 for the first write.</param>
public sealed record SaveProfileResponse(long Revision);
