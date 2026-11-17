using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks for the position of a street or address the user typed in the search bar.
/// </summary>
/// <param name="Text">What the user typed.</param>
public sealed record SearchAddressQuery(string Text) : IQuery<SearchAddressResponse>;
