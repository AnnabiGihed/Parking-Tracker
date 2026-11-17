using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.Sectors.Queries.GetOfficialListStatus;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks whether the City of Brussels sector document is still the one the app's street lists come from, for the
///              saved card. It takes no input, so it has no validator; the checker re-reads the document only when its last
///              answer is older than the recheck interval.
/// </summary>
public sealed record GetOfficialListStatusQuery : IQuery<GetOfficialListStatusResponse>;
