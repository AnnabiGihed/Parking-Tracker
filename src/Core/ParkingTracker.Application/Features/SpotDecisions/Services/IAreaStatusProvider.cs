using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Fetches the live data for an area and evaluates every street surface in it for the user's card.
/// </summary>
public interface IAreaStatusProvider
{
	#region Methods
	/// <summary>
	/// Evaluates the surfaces of an area. Unlike a single decision, an area has no "can't verify" answer: when the surfaces,
	/// the sector areas or the house numbers cannot be fetched, the failure is returned. Missing parking rules are not a
	/// failure; the surfaces are then evaluated by their colour.
	/// </summary>
	/// <param name="context">The card and its street lists.</param>
	/// <param name="box">The area, in degrees.</param>
	/// <param name="now">The current moment.</param>
	/// <param name="addressFetching">Whether house numbers are fetched for City of Brussels cards.</param>
	/// <param name="cancellationToken">Cancels the fetching.</param>
	/// <returns>The status of each surface in the area, or the failure of the first source that could not be read.</returns>
	Task<Result<IReadOnlyList<SurfaceStatus>>> LoadAsync(DecisionContext context, GeoBox box, DateTimeOffset now, AddressFetching addressFetching, CancellationToken cancellationToken);
	#endregion
}
