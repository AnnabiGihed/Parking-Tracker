using ParkingTracker.Contracts.Features.Profiles;

namespace ParkingTracker.Backend.Application.Features.Accounts.Queries.GetProfile;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The profile of an account as it is stored: the decrypted document and the revision it is stored under.
/// </summary>
/// <param name="Document">The decrypted document.</param>
/// <param name="Revision">The revision of the stored document.</param>
public sealed record GetProfileResponse(ProfileDocument Document, long Revision);
