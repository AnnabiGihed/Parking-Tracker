using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks for the nearest spots where the user's card covers parking, from live data only, one per street.
/// </summary>
/// <param name="Latitude">The latitude in degrees where the search starts.</param>
/// <param name="Longitude">The longitude in degrees where the search starts.</param>
/// <param name="Count">The maximum number of spots, at least 1.</param>
public sealed record FindFreeSpotsQuery(double Latitude, double Longitude, int Count = 3) : IQuery<IReadOnlyList<FreeSpot>>;
