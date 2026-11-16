using ParkingTracker.Application.Decisions;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Geometry;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Queries;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="FindFreeSpotsQuery"/>: with a valid card and confirmed street lists it evaluates the
///              surfaces around the start and picks the nearest covered spot per street; otherwise it finds none.
/// </summary>
public sealed class FindFreeSpotsQueryHandler : IQueryHandler<FindFreeSpotsQuery, IReadOnlyList<FreeSpot>>
{
	#region Fields
	private readonly IDecisionContextFactory contexts;
	private readonly IAreaStatusProvider areas;
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FindFreeSpotsQueryHandler"/> class.
	/// </summary>
	/// <param name="contexts">Builds the card and street-list context.</param>
	/// <param name="areas">Fetches and evaluates the surfaces of an area.</param>
	/// <param name="clock">The clock.</param>
	public FindFreeSpotsQueryHandler(IDecisionContextFactory contexts, IAreaStatusProvider areas, TimeProvider clock)
	{
		this.contexts = contexts;
		this.areas = areas;
		this.clock = clock;
	}
	#endregion

	#region IQueryHandler Implementation
	/// <inheritdoc />
	public async Task<Result<IReadOnlyList<FreeSpot>>> Handle(FindFreeSpotsQuery request, CancellationToken cancellationToken)
	{
		var now = clock.GetLocalNow();
		var context = await contexts.CreateAsync(now, cancellationToken).ConfigureAwait(false);
		if (context.IsFailure)
		{
			return Result.Failure<IReadOnlyList<FreeSpot>>(context.Error);
		}

		if (!AreaDecider.CanSearchSpots(context.Value, DateOnly.FromDateTime(now.DateTime)))
		{
			return Result.Success<IReadOnlyList<FreeSpot>>([]);
		}

		var origin = new GeoPoint(request.Latitude, request.Longitude);
		var surfaces = await areas.LoadAsync(context.Value, GeoBox.Around(origin, AreaDecider.SearchDelta), now, AddressFetching.Fetch, cancellationToken).ConfigureAwait(false);
		return surfaces.IsFailure
			? Result.Failure<IReadOnlyList<FreeSpot>>(surfaces.Error)
			: Result.Success(AreaDecider.FindFreeSpots(origin, surfaces.Value, request.Count));
	}
	#endregion
}
