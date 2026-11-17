namespace ParkingTracker.ViewModels.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the card line of the bottom sheet and the dot on the card button show.
/// </summary>
/// <param name="Dot">The colour of the dot on the card button.</param>
/// <param name="Pill">The text of the pill: where the card is valid, with the plate.</param>
/// <param name="Detail">The text on the right: validity or a warning; empty when there is no card.</param>
/// <param name="DetailIsWarning">Whether <paramref name="Detail"/> is a warning.</param>
/// <param name="RenewalLink">The e-mail to renew the card when it expires within 30 days, or <see langword="null"/>.</param>
/// <param name="ShowAddLink">Whether "Add my card" is offered, because there is no card.</param>
public sealed record CardLineView(CardDot Dot, string Pill, string Detail, bool DetailIsWarning, Uri? RenewalLink, bool ShowAddLink);
