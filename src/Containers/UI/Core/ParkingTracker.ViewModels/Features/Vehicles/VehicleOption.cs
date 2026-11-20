namespace ParkingTracker.ViewModels.Features.Vehicles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One choice of a list in the My vehicle dialog: the value the view model keeps and the readable text the user sees.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="Value">The value the view model keeps.</param>
/// <param name="Text">The text the user sees.</param>
public sealed record VehicleOption<T>(T Value, string Text);
