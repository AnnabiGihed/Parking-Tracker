using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.AddressSearch.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the search for a street or address in Brussels. Only the text the user typed leaves the phone, nothing
///              that identifies the user.
/// </summary>
public interface IAddressSearch
{
	#region Methods
	/// <summary>
	/// Searches the matches for a text inside Brussels.
	/// </summary>
	/// <param name="text">What the user typed.</param>
	/// <param name="limit">The most matches to return.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The matches, best first, empty when nothing was found; or a failure with a <c>Nominatim.*</c> code.</returns>
	Task<Result<IReadOnlyList<AddressMatch>>> SearchAsync(string text, int limit, CancellationToken cancellationToken);
	#endregion
}
