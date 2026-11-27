using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Contracts.Features.Profiles;

namespace ParkingTracker.Application.Features.ProfileSync.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The result of a merge of the local profile with the server's (ADR-0032): the document and the moment of each section.
///              A section that is absent keeps the moment of the side that cleared it.
/// </summary>
/// <param name="Document">The merged document.</param>
/// <param name="Times">The moment of each section of the merged document.</param>
public sealed record MergedProfile(ProfileDocument Document, SectionTimes Times);
