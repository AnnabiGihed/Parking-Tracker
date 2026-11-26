namespace ParkingTracker.Contracts.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The favourite places of the profile document. The list is one section: two phones merge it as a whole, the newest wins.
/// </summary>
/// <param name="UpdatedAt">The time the phone changed the list.</param>
/// <param name="Places">The favourite places, at most 50.</param>
public sealed record ProfileFavouritesSection(DateTimeOffset UpdatedAt, IReadOnlyList<ProfileFavouritePlace> Places);
