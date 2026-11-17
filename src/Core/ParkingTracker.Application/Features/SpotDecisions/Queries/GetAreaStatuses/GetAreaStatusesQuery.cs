using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.SpotDecisions.Queries.GetAreaStatuses;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks for the status of every street surface in a map area for the user's card (the map layer).
/// </summary>
/// <param name="South">The southern edge in degrees of latitude.</param>
/// <param name="West">The western edge in degrees of longitude.</param>
/// <param name="North">The northern edge in degrees of latitude.</param>
/// <param name="East">The eastern edge in degrees of longitude.</param>
/// <param name="WithAddresses">Whether house numbers are fetched for City of Brussels cards, to colour part-covered streets.</param>
public sealed record GetAreaStatusesQuery(double South, double West, double North, double East, bool WithAddresses = true) : IQuery<IReadOnlyList<SurfaceStatus>>;
