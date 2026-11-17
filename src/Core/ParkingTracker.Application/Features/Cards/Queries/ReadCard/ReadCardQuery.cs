using ParkingTracker.Application.Features.Cards.Services;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.Cards.Queries.ReadCard;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks to scan the residents' card: the user takes or picks a photo, the phone reads its text and the fields found
///              come back. It carries no input, so it has no validator.
/// </summary>
public sealed record ReadCardQuery : IQuery<ParsedCard>;
