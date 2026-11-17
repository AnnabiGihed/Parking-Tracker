using ParkingTracker.Domain.Features.MapLayers.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.MapLayers.Queries.GetMapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks for the data of the map layers that are switched on in a map area: the outline of the areas on the user's
///              card, parking spaces, roadworks and events, car parks and charging points. The street colours come from
///              <c>GetAreaStatusesQuery</c>, because they need the decision rules.
/// </summary>
/// <param name="South">The southern edge in degrees of latitude.</param>
/// <param name="West">The western edge in degrees of longitude.</param>
/// <param name="North">The northern edge in degrees of latitude.</param>
/// <param name="East">The eastern edge in degrees of longitude.</param>
/// <param name="Layers">The layers to load; <see cref="LayerSettings.Streets"/> stands for the outline of the card's areas.</param>
public sealed record GetMapLayersQuery(double South, double West, double North, double East, LayerSettings Layers) : IQuery<GetMapLayersResponse>;
