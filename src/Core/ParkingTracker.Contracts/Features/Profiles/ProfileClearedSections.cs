namespace ParkingTracker.Contracts.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The moments at which the phone cleared a section of the profile (ADR-0032). A section that is absent from the document
///              has no <c>updatedAt</c> of its own, so without this a clear could not be told from "never set" and an older value
///              on another phone would win the merge. A time is given only for a section that is absent from the same document. The
///              favourites need no entry: the list section always carries its own <c>updatedAt</c>, also when it is empty.
/// </summary>
/// <param name="Home">When the home place was cleared, or <see langword="null"/>.</param>
/// <param name="Work">When the work place was cleared, or <see langword="null"/>.</param>
/// <param name="Vehicle">When the vehicle profile was cleared, or <see langword="null"/>.</param>
/// <param name="Card">When the residents' card was cleared, or <see langword="null"/>.</param>
public sealed record ProfileClearedSections(DateTimeOffset? Home, DateTimeOffset? Work, DateTimeOffset? Vehicle, DateTimeOffset? Card);
