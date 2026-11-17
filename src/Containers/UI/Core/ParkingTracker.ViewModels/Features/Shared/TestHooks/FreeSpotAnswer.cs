namespace ParkingTracker.ViewModels.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One spot that the <c>ptTest.freeSpots</c> hook returns to the emulator test.
/// </summary>
/// <param name="Label">The street and commune as the screen prints them.</param>
/// <param name="Metres">The distance from the searched place in metres.</param>
/// <param name="Street">The street.</param>
/// <param name="Commune">The commune.</param>
public sealed record FreeSpotAnswer(string Label, double Metres, string Street, string Commune);
