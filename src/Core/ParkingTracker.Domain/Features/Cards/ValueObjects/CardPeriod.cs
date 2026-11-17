namespace ParkingTracker.Domain.Features.Cards.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The validity dates printed on a residents' parking card; the rest of the card is modelled where the
///              decision needs it.
/// </summary>
/// <param name="ValidFrom">First valid day, or <see langword="null"/> when the card has no start date.</param>
/// <param name="ValidTo">Last valid day, or <see langword="null"/> when the card has no end date.</param>
public sealed record CardPeriod(DateOnly? ValidFrom, DateOnly? ValidTo);
