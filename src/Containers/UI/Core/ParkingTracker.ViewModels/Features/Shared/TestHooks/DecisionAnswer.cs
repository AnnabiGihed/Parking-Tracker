namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the <c>ptTest.decide</c> hook returns to the emulator test: the decision as plain text, the fields the old test read
///              from <c>PT.decide</c>.
/// </summary>
/// <param name="Status">The status colour: <c>free</c>, <c>pay</c> or <c>unknown</c>.</param>
/// <param name="Title">The headline, for example <c>FREE ✓</c>.</param>
/// <param name="Detail">The explanation as plain text.</param>
/// <param name="Tariff">The tariff text, or <see langword="null"/> when there is none.</param>
/// <param name="Notes">Extra remarks as plain text.</param>
public sealed record DecisionAnswer(string Status, string Title, string Detail, string? Tariff, IReadOnlyList<string> Notes);
