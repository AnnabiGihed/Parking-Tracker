using ParkingTracker.Application.Features.Cards.Services;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.Cards.Queries.ParseCardText;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks for the card fields in a text that was already recognised, for example the text a test gives the emulator
///              hook. Scanning a photo is <c>ReadCardQuery</c>.
/// </summary>
/// <param name="Text">The recognised text.</param>
public sealed record ParseCardTextQuery(string Text) : IQuery<ParsedCard>;
