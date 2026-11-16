using ParkingTracker.Application.Sources;

namespace ParkingTracker.Infrastructure.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What every call to a live source shares: which service it is, how the data is named in failure messages and the
///              limit of time. It keeps the calls of <see cref="SourceHttp"/> short.
/// </summary>
/// <param name="Source">The service, for the failure code.</param>
/// <param name="What">The name of the data in failure messages.</param>
/// <param name="Timeout">The limit of time.</param>
internal sealed record SourceCall(SourceKind Source, string What, TimeSpan Timeout);
