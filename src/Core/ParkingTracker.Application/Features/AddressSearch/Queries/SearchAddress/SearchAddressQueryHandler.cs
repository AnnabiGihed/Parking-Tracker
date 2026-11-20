using ParkingTracker.Application.Features.AddressSearch.Abstractions;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="SearchAddressQuery"/>: asks the search port for the typed text, trimmed, and returns every
///              match, best first. Nothing found is a successful answer without a match; an unreachable service is a failure.
/// </summary>
public sealed class SearchAddressQueryHandler : IQueryHandler<SearchAddressQuery, SearchAddressResponse>
{
	#region Fields
	/// <summary>
	/// Port to the address search.
	/// </summary>
	private readonly IAddressSearch search;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SearchAddressQueryHandler"/> class.
	/// </summary>
	/// <param name="search">Searches the address.</param>
	public SearchAddressQueryHandler(IAddressSearch search)
	{
		this.search = search;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<SearchAddressResponse>> Handle(SearchAddressQuery request, CancellationToken cancellationToken)
	{
		var matches = await search.SearchAsync(request.Text.Trim(), request.Limit, cancellationToken).ConfigureAwait(false);
		if (matches.IsFailure)
		{
			return Result.Failure<SearchAddressResponse>(matches.Error);
		}

		return Result.Success(new SearchAddressResponse(matches.Value));
	}
	#endregion
}
