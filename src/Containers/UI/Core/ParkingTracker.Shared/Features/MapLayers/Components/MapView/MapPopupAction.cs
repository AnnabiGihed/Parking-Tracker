namespace ParkingTracker.Shared.Features.MapLayers.Components;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A button the user pressed in a map popup, as the map script reports it.
/// </summary>
/// <param name="Kind">The action name of the button, for example <c>waze</c>.</param>
/// <param name="Id">The value of the button: a position or an address. It comes from the page, so the receiver checks it.</param>
public sealed record MapPopupAction(string Kind, string Id);
