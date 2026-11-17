using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.Sectors.Queries.GetMailles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks for the sector areas (mailles) a card of a municipality can show, for the card form: the municipality's own
///              areas and its border zones.
/// </summary>
/// <param name="MunicipalityId">Identifier of the municipality, for example <c>ixelles</c>.</param>
public sealed record GetMaillesQuery(string MunicipalityId) : IQuery<GetMaillesResponse>;
