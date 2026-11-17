namespace ParkingTracker.ViewModels.Features.MapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One marker drawn on the map, in the compact form the map script receives.
/// </summary>
/// <param name="Lat">The latitude in degrees, rounded to six decimals.</param>
/// <param name="Lon">The longitude in degrees, rounded to six decimals.</param>
/// <param name="Style">The style key: <c>space</c> for an ordinary space, otherwise a marker with the <paramref name="Label"/>.</param>
/// <param name="Label">The emoji shown in the marker, HTML-escaped because it can come from a service, or <see langword="null"/>.</param>
/// <param name="Popup">The HTML of the popup, with the text escaped, or <see langword="null"/> for none.</param>
public sealed record MapPoint(double Lat, double Lon, string Style, string? Label, string? Popup);
