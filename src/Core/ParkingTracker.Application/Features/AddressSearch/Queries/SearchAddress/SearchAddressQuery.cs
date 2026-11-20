using Pivot.Framework.Application.Abstractions.Messaging.Queries;

namespace ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Asks for the places that match a street or address the user typed in the search bar.
/// </summary>
/// <param name="Text">What the user typed.</param>
/// <param name="Limit">The most matches to return; <see cref="DefaultLimit"/> when not given.</param>
public sealed record SearchAddressQuery(string Text, int Limit = SearchAddressQuery.DefaultLimit) : IQuery<SearchAddressResponse>
{
	#region Constants
	/// <summary>
	/// The number of matches asked for when the caller gives none.
	/// </summary>
	public const int DefaultLimit = 8;
	#endregion
}
