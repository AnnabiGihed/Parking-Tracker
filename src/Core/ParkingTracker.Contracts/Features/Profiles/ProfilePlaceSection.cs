namespace ParkingTracker.Contracts.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A saved place of the profile document: the home or the work place.
/// </summary>
/// <param name="Label">The name the user gave the place, or <see langword="null"/>.</param>
/// <param name="Latitude">The latitude in degrees, inside the Brussels box.</param>
/// <param name="Longitude">The longitude in degrees, inside the Brussels box.</param>
/// <param name="AddressText">The address as the user saw it, or <see langword="null"/>.</param>
/// <param name="UpdatedAt">The time the phone changed the place.</param>
public sealed record ProfilePlaceSection(string? Label, double Latitude, double Longitude, string? AddressText, DateTimeOffset UpdatedAt);
