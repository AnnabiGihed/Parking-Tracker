namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One chip under the status block: when the answer was read, where the position came from, or which zone it is in.
/// </summary>
/// <param name="Text">The text of the chip.</param>
/// <param name="IconName">The name of the icon, empty for none.</param>
/// <param name="Kind">How the chip looks.</param>
/// <param name="ColourHex">The colour of the dot of a zone chip, or <see langword="null"/>.</param>
public sealed record StatusChip(string Text, string IconName, ChipKind Kind, string? ColourHex = null);
