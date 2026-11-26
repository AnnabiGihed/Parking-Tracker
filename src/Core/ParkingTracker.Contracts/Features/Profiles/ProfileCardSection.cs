namespace ParkingTracker.Contracts.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The residents' card of the profile document: where it is valid, until when, and the number and plate on it.
/// </summary>
/// <param name="Municipality">The municipality that issued the card.</param>
/// <param name="Sector">The sector of the card, or <see langword="null"/>.</param>
/// <param name="ValidFrom">The first day the card is valid, or <see langword="null"/>.</param>
/// <param name="ValidTo">The last day the card is valid, or <see langword="null"/>.</param>
/// <param name="Tolerances">The tolerance zones the card gives, at most 20.</param>
/// <param name="Mailles">The mailles the card is valid in, at most 20.</param>
/// <param name="Lims">The low-emission zones the card gives access to, at most 20.</param>
/// <param name="Number">The card number, or <see langword="null"/>.</param>
/// <param name="Plate">The number plate on the card, or <see langword="null"/>.</param>
/// <param name="UpdatedAt">The time the phone changed the card.</param>
public sealed record ProfileCardSection(
	string Municipality,
	string? Sector,
	DateOnly? ValidFrom,
	DateOnly? ValidTo,
	IReadOnlyList<string> Tolerances,
	IReadOnlyList<string> Mailles,
	IReadOnlyList<string> Lims,
	string? Number,
	string? Plate,
	DateTimeOffset UpdatedAt);
