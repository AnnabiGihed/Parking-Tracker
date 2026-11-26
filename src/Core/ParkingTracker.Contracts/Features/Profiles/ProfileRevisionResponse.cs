namespace ParkingTracker.Contracts.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer to a saved profile: the revision the document is now stored under, the same number the <c>ETag</c> header
///              carries. The document is not sent back, because the caller has it.
/// </summary>
/// <param name="Revision">The revision of the stored document, 1 for the first write.</param>
public sealed record ProfileRevisionResponse(long Revision);
