namespace ParkingTracker.Domain.Features.MapLayers.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Which map layers the user switched on. It is saved as the <c>pt.layers</c> value of the first version of the app,
///              and a layer missing from the saved value keeps its default.
/// </summary>
/// <param name="Streets">Where the card works, per street.</param>
/// <param name="Spaces">Individual parking spaces and reserved spaces.</param>
/// <param name="Works">Roadworks and traffic events.</param>
/// <param name="CarParks">Public car parks.</param>
/// <param name="Chargers">Charging points for electric vehicles.</param>
public sealed record LayerSettings(bool Streets = true, bool Spaces = true, bool Works = true, bool CarParks = false, bool Chargers = false);
