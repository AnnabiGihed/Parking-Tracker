namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The card fields that the <c>ptTest.parseCardText</c> hook returns to the emulator test, named and written as the
///              first version of the app's <c>parseCardText</c> did: dates as <c>yyyy-MM-dd</c>, a field that was not found is
///              <see langword="null"/>.
/// </summary>
/// <param name="Number">The card number, or <see langword="null"/>.</param>
/// <param name="Plate">The plate, or <see langword="null"/>.</param>
/// <param name="From">The first day of validity, or <see langword="null"/>.</param>
/// <param name="To">The last day of validity, or <see langword="null"/>.</param>
/// <param name="Municipality">The identifier of the municipality, or <see langword="null"/>.</param>
/// <param name="Sector">The identifier of the City sector, or <see langword="null"/>.</param>
/// <param name="Tolerances">The tolerance codes found; empty when none.</param>
public sealed record CardTextAnswer(string? Number, string? Plate, string? From, string? To, string? Municipality, string? Sector, IReadOnlyList<string> Tolerances);
