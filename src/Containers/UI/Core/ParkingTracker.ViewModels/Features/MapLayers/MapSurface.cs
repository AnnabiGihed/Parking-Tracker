namespace ParkingTracker.ViewModels.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One area drawn on the map, in the compact form the map script receives: a style key that the script turns into
///              a colour, the polygons as latitude and longitude pairs rounded to six decimals, and the popup text already escaped.
/// </summary>
/// <param name="Style">The style key, for example <c>covered</c>, <c>pay</c>, <c>area</c> or <c>work</c>.</param>
/// <param name="Shape">The polygons, each a list of rings of <c>[latitude, longitude]</c> pairs.</param>
/// <param name="Popup">The HTML of the popup, with the text escaped, or <see langword="null"/> for none.</param>
public sealed record MapSurface(string Style, IReadOnlyList<IReadOnlyList<IReadOnlyList<double[]>>> Shape, string? Popup);
